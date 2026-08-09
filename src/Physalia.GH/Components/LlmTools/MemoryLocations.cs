// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Memory;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves the physical directories the <see cref="MemoryTool"/> reads and writes, under
/// <c>Files/MEMORIES</c> beside the plug-in (the same <c>Files</c> tree the rest of Physalia keeps
/// user-alterable content in). The global memory is a single shared folder; each Grasshopper document
/// gets its own local folder keyed by its file so per-document memory follows the .gh file across
/// sessions.
///
/// <para>These are the folder names on disk only. The model addresses memory through a virtual
/// <c>/memories/global</c> and <c>/memories/local</c> scheme (see <c>MemoryStore</c>), which is
/// matched case-insensitively and is unaffected by what these folders are called.</para>
/// </summary>
internal static class MemoryLocations
{
    private const string UntitledKey = "untitled";

    /// <summary>
    /// Returns the global and local memory directories for the given document. The local directory is
    /// keyed by the document's file so it persists per .gh file; an unsaved document shares an
    /// "untitled" folder for the session.
    /// </summary>
    /// <param name="document">The document the memory is scoped to, or null.</param>
    /// <returns>The resolved global and local memory roots.</returns>
    internal static MemoryRoots ResolveRoots(GH_Document? document)
    {
        string root = MemoriesRoot();
        string global = Path.Combine(root, "GLOBAL");
        string local = Path.Combine(root, "LOCAL", DocumentKey(document));
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

    // A stable, filesystem-safe key for the document's local memory folder: the sanitized file name
    // plus a short hash of the full path (so identically named files in different folders never share
    // a memory). Unsaved documents share the "untitled" folder for the session.
    private static string DocumentKey(GH_Document? document)
    {
        string? path = document?.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return UntitledKey;
        }

        string name = Sanitize(Path.GetFileNameWithoutExtension(path));
        if (name.Length == 0)
        {
            name = UntitledKey;
        }

        return $"{name}-{ShortHash(path!)}";
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

    // First 8 hex chars of the SHA-256 of the normalized path — deterministic across sessions (unlike
    // string.GetHashCode, which is randomized per process in .NET Core).
    private static string ShortHash(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace('\\', '/').ToLowerInvariant()));
        var sb = new StringBuilder(8);
        for (int i = 0; i < 4; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }

        return sb.ToString();
    }
}
