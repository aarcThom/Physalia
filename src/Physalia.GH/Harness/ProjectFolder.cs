// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.IO;
using System.Reflection;
using Grasshopper.Kernel;
using Physalia.Core.Naming;

namespace Physalia.GH.Harness;

/// <summary>
/// Where a pipeline's own files live: downloads, site data, reference PDFs, anything the work needs
/// that is not in the definition.
///
/// <para>One folder per harness, named after the harness, under <c>Files/PROJECT_FILES</c>. Everything
/// that reads or writes project files goes through here — the Project Folder grounder, the download
/// tool, the file reader, and Read PDF, whose own library folder was folded into this one. Having a
/// single resolver is what lets a harness's whole working set be packaged into a <c>.phy</c> and
/// handed to somebody else.</para>
///
/// <para>The path rules themselves are pure and live in <see cref="ProjectPaths"/>; this supplies the
/// two roots it cannot know — where the plug-in is installed, and where the user's document is saved
/// — and owns the one side effect, which is moving the folder when a harness is renamed.</para>
/// </summary>
internal static class ProjectFolder
{
    /// <summary>
    /// The folder under <c>Files</c> holding every harness's project folder.
    /// </summary>
    internal const string RootFolderName = "PROJECT_FILES";

    /// <summary>
    /// Resolves the project folder for a harness, honouring a typed override.
    /// </summary>
    /// <param name="harness">The harness whose folder is wanted; null resolves only a typed value.</param>
    /// <param name="typed">A typed override, or null/blank for the harness's own folder.</param>
    /// <param name="hostDocument">
    /// The user's canvas, whose saved path anchors a relative override. Pass the HOST document —
    /// a harness sub-document has no file path of its own.
    /// </param>
    /// <returns>The resolution, which may carry a problem instead of a path.</returns>
    internal static ProjectPathResolution Resolve(
        HarnessComponent? harness,
        string? typed,
        GH_Document? hostDocument)
    {
        string defaultKey = harness is null ? "unnamed" : DefaultKeyFor(harness);
        return ProjectPaths.Resolve(typed, defaultKey, Root(), DocumentFolder(hostDocument));
    }

    /// <summary>
    /// The folder name a harness uses when nothing is typed: its nickname, which is a four-word key
    /// until somebody renames it.
    /// </summary>
    /// <param name="harness">The harness.</param>
    /// <returns>The sanitized folder name.</returns>
    internal static string DefaultKeyFor(HarnessComponent harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        return ProjectPaths.FolderKey(harness.NickName);
    }

    /// <summary>
    /// The absolute default project folder for a harness, whatever any node has typed.
    /// </summary>
    /// <param name="harness">The harness.</param>
    /// <returns>The folder path; not created.</returns>
    internal static string PathFor(HarnessComponent harness) =>
        Path.Combine(Root(), DefaultKeyFor(harness));

    /// <summary>
    /// The absolute default project folder for a given folder key.
    /// </summary>
    /// <param name="key">An already-sanitized folder key.</param>
    /// <returns>The folder path; not created.</returns>
    internal static string PathForKey(string key) => Path.Combine(Root(), key);

    /// <summary>
    /// Moves a harness's project folder to follow a rename.
    ///
    /// <para><b>Why a move rather than a stamp.</b> The folder is named after the harness, so leaving
    /// it behind on a rename orphans everything already downloaded into it — the files are still on
    /// disk but nothing looks there any more. Moving keeps the two in step, and because the key is
    /// always derived from the current name rather than frozen, an undone rename moves the folder
    /// back on its own.</para>
    ///
    /// <para>Four things it must not do, each of which was a way to lose files:</para>
    /// <list type="bullet">
    /// <item><description>Never move onto an existing folder. That is another harness's project, or
    /// the leavings of an older one; merging two projects silently is worse than not moving.</description></item>
    /// <item><description>Never treat a failed move as done. A file open in Rhino or held by a virus
    /// scanner blocks the move, and the caller keeps using the OLD key until it succeeds — so the
    /// pipeline goes on working and tries again later.</description></item>
    /// <item><description>Never move a folder this harness does not own. A pasted harness carries the
    /// name it was copied from; the caller establishes ownership before calling.</description></item>
    /// <item><description>Never move when the source does not exist. There is nothing to preserve,
    /// and creating the destination here would put an empty folder on disk for a harness that may
    /// never download anything.</description></item>
    /// </list>
    /// </summary>
    /// <param name="fromKey">The folder key the harness used to have.</param>
    /// <param name="toKey">The folder key it has now.</param>
    /// <param name="error">Why the move did not happen, when it was attempted and failed.</param>
    /// <returns>
    /// True when the folder now sits under <paramref name="toKey"/> — including when there was
    /// nothing to move, since the caller's bookkeeping should advance either way.
    /// </returns>
    internal static bool TryMove(string fromKey, string toKey, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrEmpty(fromKey)
            || string.IsNullOrEmpty(toKey)
            || string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string source = PathForKey(fromKey);
        string destination = PathForKey(toKey);

        if (!Directory.Exists(source))
        {
            return true;
        }

        if (Directory.Exists(destination))
        {
            error = "\"" + toKey + "\" already has a project folder, so the files in \"" + fromKey
                + "\" were left where they are. Rename to something else, or merge the two folders by hand.";
            return false;
        }

        try
        {
            Directory.Move(source, destination);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "The project folder could not be renamed to \"" + toKey + "\": " + ex.Message
                + " Physalia is still using \"" + fromKey + "\" and will try again.";
            return false;
        }
    }

    /// <summary>
    /// Ensures a folder exists, reporting rather than throwing when it cannot be made.
    /// </summary>
    /// <param name="path">The folder to create.</param>
    /// <param name="error">Why it could not be created.</param>
    /// <returns>True when the folder exists afterwards.</returns>
    internal static bool TryEnsure(string? path, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "No project folder has been resolved.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "The project folder could not be created: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// <c>Files/PROJECT_FILES</c> beside the executing assembly.
    /// </summary>
    /// <returns>The project-files root.</returns>
    internal static string Root()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return assemblyDir is null
            ? RootFolderName
            : Path.Combine(assemblyDir, "Files", RootFolderName);
    }

    // The folder the user's document is saved in, or null when it has never been saved. Deliberately
    // the HOST document: a harness sub-document has no file path, so asking it would make every
    // relative path unresolvable from inside a harness — which is where they are typed.
    private static string? DocumentFolder(GH_Document? hostDocument)
    {
        string? file = hostDocument?.FilePath;
        return string.IsNullOrEmpty(file) ? null : Path.GetDirectoryName(file);
    }
}
