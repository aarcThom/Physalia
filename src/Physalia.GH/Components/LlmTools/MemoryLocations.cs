// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Physalia.Core.Memory;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves the physical directories the <see cref="MemoryTool"/> reads and writes, under
/// <c>Files/MEMORIES</c> beside the plug-in (the same <c>Files</c> tree the rest of Physalia keeps
/// user-alterable content in). The global memory is a single shared folder; the local memory lives in
/// a folder the user NAMES, on the Memory tool's own Memory Folder input.
///
/// <para><b>Why it is named and not derived.</b> Two derivations were tried and both failed the same
/// way — silently, by defaulting. Keying on the .gh file meant memory followed the document rather
/// than the pipeline, so a harness saved out as a preset left its notes behind. Keying on the
/// harness's name looked better but a harness is called "Harness" until someone renames it, so every
/// unrenamed pipeline quietly shared one folder called <c>Harness</c> and nobody could see that it
/// had happened. A derived key is only as good as the thing it derives from, and both of those things
/// have defaults that no one notices. So the name is now typed in, it travels with the node (it is
/// ordinary internalized param data, saved in the file and carried inside a preset), and the fallback
/// when nothing is typed is the node's own instance id — unique, stable across save/load, and
/// obviously not a name, so it cannot be mistaken for one or collide with anybody else's.</para>
///
/// <para>The name is a folder name, so it is sanitized here and nowhere else: path separators,
/// invalid characters and whitespace become dashes, and leading/trailing dots and dashes are trimmed,
/// which is also what keeps <c>..</c> from walking out of the memory root.</para>
///
/// <para>These are the folder names on disk only. The model addresses memory through a virtual
/// <c>/memories/global</c> and <c>/memories/local</c> scheme (see <c>MemoryStore</c>), which is
/// matched case-insensitively and is unaffected by what these folders are called.</para>
/// </summary>
internal static class MemoryLocations
{
    // Last-resort folder for a key that sanitizes away to nothing (a name of only dots or slashes).
    // Unreachable in normal use: the Memory tool always passes its instance id when the input is
    // blank, and a guid survives sanitizing untouched.
    private const string UnnamedKey = "unnamed";

    /// <summary>
    /// Returns the global and local memory directories, the local one named by the given folder key.
    /// </summary>
    /// <param name="folderName">
    /// The user's Memory Folder value, or the caller's fallback when they left it blank. Sanitized
    /// into a single folder name; never a path.
    /// </param>
    /// <returns>The resolved global and local memory roots.</returns>
    internal static MemoryRoots ResolveRoots(string? folderName)
    {
        string root = MemoriesRoot();
        string global = Path.Combine(root, "GLOBAL");
        string local = Path.Combine(root, "LOCAL", FolderKey(folderName));
        return new MemoryRoots(global, local);
    }

    /// <summary>
    /// Reduces a user-typed memory folder name to a single safe folder name. Exposed so a caller can
    /// show the user the folder their name actually resolves to.
    /// </summary>
    /// <param name="folderName">The typed name, which may be null, blank or full of nonsense.</param>
    /// <returns>The folder name used on disk.</returns>
    internal static string FolderKey(string? folderName)
    {
        string key = Sanitize(folderName ?? string.Empty);
        return key.Length == 0 ? UnnamedKey : key;
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

    // Anything that cannot be in a Windows file name — the path separators included, which is what
    // makes this a containment guard as well as a tidy-up — becomes a dash. Dots survive in the
    // middle ("v1.2" stays readable) but are trimmed off the ends, so ".." and "." cannot address a
    // parent and a trailing dot cannot produce a name Windows refuses to create.
    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c);
        }

        return sb.ToString().Trim('-', '.');
    }
}
