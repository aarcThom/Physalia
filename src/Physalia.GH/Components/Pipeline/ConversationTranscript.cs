// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Recording;

namespace Physalia.GH.Components;

/// <summary>
/// Reads and writes a pipeline's conversation in its project folder.
///
/// <para><b>The project folder, not the .gh file.</b> A transcript is project material, exactly like
/// a downloaded LiDAR tile or an attached PDF, and it belongs where the rest of that lives — so it
/// ships inside a <c>.phy</c> for free, and a colleague opening a shared pipeline gets the reasoning
/// as well as the wiring. Serializing it onto the component instead would put images inside a
/// <c>.gh</c>, which is a file people copy and email far more casually than a project folder.</para>
///
/// <para><b>Autosaved every turn, resumed only on request.</b> Saving continuously is what makes a
/// crash or a Rhino restart cost nothing. Loading automatically would be the opposite of a favour:
/// a pipeline shared across a firm would arrive with its author's conversation already in it, paid
/// for on the very next call, and nobody would have asked for that. So a saved transcript is OFFERED
/// — the Conversation Log stays empty and says one is there.</para>
///
/// <para><b>Image files are written once.</b> Their keys are derived from the turn and block index
/// and the conversation is append-only, so a key that already exists on disk is the same image. That
/// is what keeps a per-turn autosave from rewriting every picture in the history each time.</para>
/// </summary>
internal static class ConversationTranscript
{
    /// <summary>The transcript file's name inside the project folder.</summary>
    internal const string FileName = "conversation.json";

    /// <summary>The folder the transcript's images are written into, beside the transcript.</summary>
    internal const string ImageFolder = "conversation-images";

    /// <summary>
    /// Where a component's transcript lives, or null when no project folder can be resolved.
    /// </summary>
    /// <param name="component">The Conversation Log asking.</param>
    /// <returns>The absolute transcript path, or null.</returns>
    internal static string? PathFor(GH_Component component)
    {
        // The harness's own folder, deliberately with no input of its own to override it: a
        // transcript that could point somewhere other than the pipeline's files would eventually
        // point at another pipeline's.
        Physalia.Core.Naming.ProjectPathResolution resolution = ProjectFolderInput.Resolve(component, null);

        return resolution.IsResolved && resolution.FullPath is { Length: > 0 } folder
            ? Path.Combine(folder, FileName)
            : null;
    }

    /// <summary>
    /// How many turns the saved transcript holds, or null when there is none to resume.
    ///
    /// <para>Counted from the file rather than by parsing it fully, because this is asked on the chat
    /// window's own tick and a full parse would decode every image reference each time.</para>
    /// </summary>
    /// <param name="component">The Conversation Log asking.</param>
    /// <returns>The turn count, or null.</returns>
    internal static int? SavedTurns(GH_Component component)
    {
        string? path = PathFor(component);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);

            return document.RootElement.TryGetProperty("turns", out JsonElement turns)
                && turns.ValueKind == JsonValueKind.Array
                    ? turns.GetArrayLength()
                    : null;
        }
        catch (Exception)
        {
            // An unreadable transcript offers nothing to resume. Reported where it matters (the load
            // path says why); here the honest answer is simply "nothing to offer".
            return null;
        }
    }

    /// <summary>
    /// Writes a conversation, creating the folder if need be.
    /// </summary>
    /// <param name="component">The Conversation Log asking.</param>
    /// <param name="conversation">The conversation to write.</param>
    /// <returns>Null on success, or why it could not be written.</returns>
    internal static string? Save(GH_Component component, Conversation conversation)
    {
        string? path = PathFor(component);
        if (path is null)
        {
            return "No project folder could be resolved, so the conversation was not saved.";
        }

        try
        {
            string folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);

            ConversationArchive archive = ConversationArchiver.Write(conversation);

            if (archive.Images.Count > 0)
            {
                string images = Path.Combine(folder, ImageFolder);
                Directory.CreateDirectory(images);

                foreach (ArchivedImage image in archive.Images)
                {
                    string file = Path.Combine(images, image.Key);

                    // Written once: see the class remarks. Keys are derived from position in an
                    // append-only history, so an existing file is the same image.
                    if (!File.Exists(file))
                    {
                        File.WriteAllBytes(file, image.Bytes);
                    }
                }
            }

            // Written to a temp file and moved into place, so a crash mid-write cannot leave a
            // half-written transcript where a complete one used to be — the same discipline the
            // download path uses.
            string temp = path + ".tmp";
            File.WriteAllText(temp, archive.Json);
            File.Move(temp, path, overwrite: true);

            // A Folder Watcher armed on this project folder must not report the pipeline's own
            // transcript back to the model as news; that would be a round per turn, forever.
            PipelineFileWrites.Record(path);

            return null;
        }
        catch (Exception ex)
        {
            return $"The conversation could not be saved: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads the saved conversation back.
    /// </summary>
    /// <param name="component">The Conversation Log asking.</param>
    /// <returns>The conversation, or why it could not be read.</returns>
    internal static Result<Conversation, string> Load(GH_Component component)
    {
        string? path = PathFor(component);
        if (path is null)
        {
            return new Result<Conversation, string>.Err("No project folder could be resolved.");
        }

        if (!File.Exists(path))
        {
            return new Result<Conversation, string>.Err("There is no saved conversation in this pipeline's project folder.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return new Result<Conversation, string>.Err($"The transcript could not be read: {ex.Message}");
        }

        string folder = Path.Combine(Path.GetDirectoryName(path)!, ImageFolder);

        return ConversationArchiver.Read(json, key => LoadImage(folder, key));
    }

    private static ArchivedImage? LoadImage(string folder, string key)
    {
        // Contained the same way every model-supplied name is: reduced to one segment, so a key from
        // a hand-edited or third-party transcript cannot climb out of the images folder.
        string name = Path.GetFileName(key);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string file = Path.Combine(folder, name);

        try
        {
            return File.Exists(file)
                ? new ArchivedImage(name, MediaTypeFor(name), File.ReadAllBytes(file))
                : null;
        }
        catch (Exception)
        {
            // A missing or unreadable picture costs its block and nothing else — the brief survives.
            return null;
        }
    }

    private static string MediaTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}
