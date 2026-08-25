// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Physalia.Core.Memory;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves the physical directories the <see cref="MemoryTool"/> reads and writes, under
/// <c>Files/MEMORIES</c> beside the plug-in (the same <c>Files</c> tree the rest of Physalia keeps
/// user-alterable content in). The global memory is a single shared folder; the local memory belongs
/// to the <b>harness</b> the tool lives in, keyed by that harness's NAME.
///
/// <para><b>Why the name, and why not the document.</b> Local memory used to be filed under the .gh
/// file the tool was running in, so it followed the user's document. A harness is the unit of work
/// here — one pipeline, one line of reasoning, shipped as a preset and copied between files — and its
/// memory is part of what it knows how to do, so it must travel with the pipeline rather than with
/// whatever file the pipeline was dropped into. Keying on the name is what makes that work: the same
/// harness name in a second document reaches the same notes, which is the point. The consequences are
/// deliberate and both directions of the same rule — <b>renaming a harness starts a fresh local
/// memory</b> (the old folder is left on disk, not migrated), and <b>two harnesses sharing a name
/// share their notes</b>. The name IS the key; rename to separate, match to share.</para>
///
/// <para>These are the folder names on disk only. The model addresses memory through a virtual
/// <c>/memories/global</c> and <c>/memories/local</c> scheme (see <c>MemoryStore</c>), which is
/// matched case-insensitively and is unaffected by what these folders are called.</para>
/// </summary>
internal static class MemoryLocations
{
    // Local memory for a tool node standing on the user's canvas rather than inside a harness. It is
    // legal to place one there (see the harness residency note in CLAUDE.md); there is simply no
    // harness identity to file the notes under, so they all share this one folder.
    private const string UnharnessedKey = "unharnessed";

    /// <summary>
    /// Returns the global and local memory directories for the given harness. The local directory is
    /// keyed by the harness's name, so it travels with the pipeline; a tool placed outside any
    /// harness falls back to a shared "unharnessed" folder.
    /// </summary>
    /// <param name="harness">The harness the memory tool lives in, or null when it lives on the canvas.</param>
    /// <returns>The resolved global and local memory roots.</returns>
    internal static MemoryRoots ResolveRoots(HarnessComponent? harness)
    {
        string root = MemoriesRoot();
        string global = Path.Combine(root, "GLOBAL");
        string local = Path.Combine(root, "LOCAL", HarnessKey(harness));
        return new MemoryRoots(global, local);
    }

    // Files/MEMORIES beside the executing assembly. Falls back to a "MEMORIES" folder in the current
    // directory if the assembly location is unknown (should not happen in a loaded plug-in).
    private static string MemoriesRoot()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return assemblyDir is null
            ? "MEMORIES"
            : Path.Combine(assemblyDir, "Files", "MEMORIES");
    }

    // A filesystem-safe key for the harness's local memory folder: its nickname, sanitized. No hash
    // is mixed in — unlike the document path this replaced, the name is not meant to be unique, it is
    // meant to be MATCHED, so that the same harness carried into another file finds its own notes.
    private static string HarnessKey(HarnessComponent? harness)
    {
        string name = Sanitize(harness?.NickName ?? string.Empty);
        return name.Length == 0 ? UnharnessedKey : name;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c);
        }

        return sb.ToString().Trim('-');
    }
}
