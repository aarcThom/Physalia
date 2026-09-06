// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Packaging;

namespace Physalia.GH.Harness;

/// <summary>
/// The preset library on disk: <c>Files/PRESETS</c> beside the plug-in, divided by where each preset
/// came from.
///
/// <para>A preset is a <c>.phy</c> package: one harness's pipeline, its name, its description, the
/// text its chat window opens with, and the project files it works on. Loading one adds a NEW harness
/// holding it. Plain <c>.gh</c> files are still listed and still load — that is what the presets
/// shipped with earlier builds are, and it keeps the format honest, since a <c>.phy</c> is a zip with
/// exactly such a file inside it.</para>
///
/// <list type="bullet">
/// <item><description><b>Physalia</b> — the pipelines shipped with the plug-in.</description></item>
/// <item><description><b>User</b> — harnesses the user saved themselves.</description></item>
/// <item><description><b>Community</b> — shared pipelines; the folder exists so the shape is settled,
/// but nothing populates it yet.</description></item>
/// </list>
///
/// <para>Nothing outside these three folders is listed — a stray <c>.gh</c> dropped in the PRESETS
/// root is ignored.</para>
/// </summary>
internal static class PresetLibrary
{
    /// <summary>Folder holding the presets shipped with the plug-in.</summary>
    internal const string PhysaliaFolder = "Physalia";

    /// <summary>Folder holding the user's own saved harnesses — the target of "Save as Preset".</summary>
    internal const string UserFolder = "User";

    /// <summary>Folder reserved for shared pipelines. Not populated yet.</summary>
    internal const string CommunityFolder = "Community";

    // Listing order: what we ship first, then the user's own, then the community. Also the order the
    // three folders are created in.
    private static readonly string[] Folders = { PhysaliaFolder, UserFolder, CommunityFolder };

    /// <summary>
    /// Gets the preset root, <c>Files/PRESETS</c> beside the assembly. Falls back to a relative path
    /// when the assembly location is unavailable (single-file publish), matching how the other
    /// <c>Files</c> lookups in the plug-in degrade.
    /// </summary>
    internal static string RootDir
    {
        get
        {
            string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return assemblyDir is null
                ? "PRESETS"
                : Path.Combine(assemblyDir, "Files", "PRESETS");
        }
    }

    /// <summary>
    /// Creates the three preset folders if they are missing, so they are there to be browsed (and
    /// dropped into) before anything has been saved. Called once at plug-in load; failures are
    /// ignored, since a read-only install is not a reason to refuse to run.
    /// </summary>
    internal static void EnsureFolders()
    {
        foreach (string folder in Folders)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(RootDir, folder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing to do — Enumerate simply finds no presets in a folder that cannot exist.
            }
        }
    }

    /// <summary>
    /// Lists every preset in the library, grouped folder by folder in listing order and sorted by
    /// name within each.
    /// </summary>
    /// <returns>The presets found, or an empty list when the library is missing or unreadable.</returns>
    internal static IReadOnlyList<PresetEntry> Enumerate()
    {
        var result = new List<PresetEntry>();
        string root = RootDir;

        foreach (string folder in Folders)
        {
            string dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                // Both formats are listed side by side. A .phy is what saving writes now; the .gh
                // presets shipped with earlier builds keep working, and one dropped in by hand still
                // loads — the library is a folder people put files in, so it has to read what is
                // there rather than only what this version writes.
                files = Directory.EnumerateFiles(dir, "*.gh")
                    .Concat(Directory.EnumerateFiles(dir, "*" + PhyPackage.Extension))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                long ticks;
                try
                {
                    ticks = File.GetLastWriteTimeUtc(path).Ticks;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                // Forward slash regardless of platform: this is a wire value the chat UI hands back
                // verbatim, not a path to be composed with.
                result.Add(new PresetEntry($"{folder}/{name}", folder, name, ticks));
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves a preset's wire value (<c>folder/name.gh</c>) to a full path on disk.
    ///
    /// <para>Resolution is by MATCH against the enumerated library, not by composing the supplied
    /// string into a path — so no input, however hostile, can escape the preset folders. An unknown
    /// value simply resolves to nothing.</para>
    /// </summary>
    /// <param name="relativePath">The wire value handed back by the chat UI.</param>
    /// <returns>The full path, or null when it names no preset in the library.</returns>
    internal static string? Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        PresetEntry? match = Enumerate()
            .FirstOrDefault(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : Path.Combine(RootDir, match.Folder, match.FileName);
    }

    /// <summary>
    /// Reads a preset's description — what the chat window's gallery shows beside it.
    ///
    /// <para>It comes out of the package manifest, which costs one small entry from the archive
    /// directory and never touches the document. That replaced a considerably worse arrangement: the
    /// description used to be the text of a Harness Notes component sitting INSIDE the pipeline, dug
    /// out by walking the Grasshopper archive as raw data and matching a component type id. The
    /// walking was necessary — instantiating every component in a preset to read one string would
    /// have run their placement hooks — but the arrangement it served was the wrong one, since a
    /// harness's own metadata does not belong to a component inside it.</para>
    ///
    /// <para>A legacy <c>.gh</c> preset has no description. None of the shipped ones ever carried a
    /// notes component, so nothing is lost by not looking for one.</para>
    /// </summary>
    /// <param name="path">Full path to the preset file.</param>
    /// <returns>The description, or null when the preset has none (or cannot be read).</returns>
    internal static string? ReadDescription(string path)
    {
        if (!PhyPackage.IsPackage(path))
        {
            return null;
        }

        return PhyPackage.ReadManifest(path).IsOk(out PhyManifest? manifest, out _)
            ? manifest.Description
            : null;
    }

    /// <summary>
    /// Works out where a user-saved preset of the given name would go, creating the User folder if
    /// needed. Does not write anything and does not care whether the file already exists — the
    /// caller decides what to do about that.
    /// </summary>
    /// <param name="requestedName">The name the user typed, with or without an extension.</param>
    /// <param name="path">The full path to write to.</param>
    /// <param name="error">Why no path could be produced.</param>
    /// <returns>True when a path was produced.</returns>
    internal static bool TryResolveUserPresetPath(string requestedName, out string path, out string error)
    {
        path = string.Empty;
        error = string.Empty;

        // Take only the file name and strip anything a file name cannot hold, so a typed name can
        // never redirect the write somewhere else.
        string name = Path.GetFileName(requestedName?.Trim() ?? string.Empty);
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid.ToString(), string.Empty);
        }

        // Either extension is stripped, so typing "site.gh" or "site.phy" both save as "site.phy".
        // What gets written is decided here, not by what the user typed.
        foreach (string extension in new[] { PhyPackage.Extension, ".gh" })
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - extension.Length);
                break;
            }
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            error = "That name has no usable characters in it.";
            return false;
        }

        string dir = Path.Combine(RootDir, UserFolder);
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"The user preset folder could not be created: {ex.Message}";
            return false;
        }

        path = Path.Combine(dir, name + PhyPackage.Extension);
        return true;
    }

    /// <summary>
    /// Archives a harness's sub-document to the bytes a <c>.gh</c> file would hold.
    ///
    /// <para>Archives directly rather than through <c>GH_DocumentIO</c>, and with
    /// <c>rememberPath: false</c> semantics, so saving leaves no trace on the live document: no
    /// stamped FilePath, no entry in Grasshopper's recent-files list. The chunk name matches what
    /// <see cref="HarnessComponent.ReadDocumentFile"/> reads and what Grasshopper itself writes, so
    /// these bytes are an ordinary Grasshopper definition — which is what lets a <c>.phy</c> be
    /// unzipped and the pipeline inside opened by hand.</para>
    /// </summary>
    /// <param name="contents">The document to archive — a harness's sub-document.</param>
    /// <param name="bytes">The archived document.</param>
    /// <param name="error">Why the archive failed.</param>
    /// <returns>True when the document was archived.</returns>
    internal static bool TryArchive(GH_Document contents, out byte[] bytes, out string error)
    {
        ArgumentNullException.ThrowIfNull(contents);
        bytes = Array.Empty<byte>();
        error = string.Empty;

        try
        {
            var archive = new GH_Archive();
            if (!archive.AppendObject(contents, "Definition"))
            {
                error = "The harness could not be archived.";
                return false;
            }

            bytes = archive.Serialize_Binary();
            if (bytes is null || bytes.Length == 0)
            {
                error = "Grasshopper produced an empty archive.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Writes a harness out as a <c>.phy</c> package.
    /// </summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="manifest">The harness's name, description and chat text.</param>
    /// <param name="contents">The harness's sub-document.</param>
    /// <param name="files">Project files to carry, or null for none.</param>
    /// <param name="bytesWritten">How big the package turned out.</param>
    /// <param name="error">Why the write failed.</param>
    /// <returns>True when the package was written.</returns>
    internal static bool TryWritePackage(
        string path,
        PhyManifest manifest,
        GH_Document contents,
        IReadOnlyList<PhyPackageFile>? files,
        out long bytesWritten,
        out string error)
    {
        bytesWritten = 0;

        if (!TryArchive(contents, out byte[] document, out error))
        {
            return false;
        }

        if (PhyPackage.Write(path, manifest, document, files).IsOk(out long written, out string? failure))
        {
            bytesWritten = written;
            return true;
        }

        error = failure;
        return false;
    }
}

/// <summary>
/// One preset in the library.
/// </summary>
/// <param name="RelativePath">Wire value handed to (and back from) the chat UI: <c>folder/name.gh</c>.</param>
/// <param name="Folder">Which of the three library folders it came from.</param>
/// <param name="FileName">The file name with extension.</param>
/// <param name="WriteTicks">Last-write time, used to notice edits without re-reading the files.</param>
internal sealed record PresetEntry(string RelativePath, string Folder, string FileName, long WriteTicks);
