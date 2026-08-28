// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves the folder of standing reference PDFs a <see cref="ReadPdf"/> node reads, named on that
/// node's own PDF Folder input.
///
/// <para>Two spellings are accepted, and the difference is deliberate. A BARE NAME resolves under
/// <c>Files/PDFS</c> beside the plug-in, sanitized to a single folder name exactly as
/// <see cref="MemoryLocations"/> does — that name is ordinary internalized param data, so it is
/// saved in the .gh and carried inside a preset, which is what lets a pipeline ship configured to
/// read its own reference set. A ROOTED PATH is used as it stands, because the reference set an
/// architectural practice actually wants to point at is a network share that already exists and is
/// not going to be copied into a plug-in folder.</para>
///
/// <para>Sanitizing applies to the bare-name case only, and that is where the containment guard
/// lives: separators and invalid characters become dashes and leading dots are trimmed, so a name
/// can never walk out of the PDF root. A rooted path is not sanitized because it is not a name —
/// it is a location the user typed on purpose, and the tool reads from it without writing.</para>
/// </summary>
internal static class PdfLocations
{
    /// <summary>
    /// Last-resort folder for a name that sanitizes away to nothing.
    /// </summary>
    private const string UnnamedKey = "unnamed";

    /// <summary>
    /// Resolves the PDF folder a node should read, or null when its input is blank.
    /// </summary>
    /// <param name="folderName">The node's PDF Folder value.</param>
    /// <returns>The absolute directory to read, or null when nothing was configured.</returns>
    internal static string? Resolve(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        string typed = folderName.Trim();

        // A rooted path is a location, not a name: honour it verbatim.
        if (IsRootedPath(typed))
        {
            return typed;
        }

        return Path.Combine(PdfsRoot(), FolderKey(typed));
    }

    /// <summary>
    /// Lists the PDFs in a resolved folder, newest name order, bounded so a folder pointed at a
    /// whole project archive cannot stall a solve.
    /// </summary>
    /// <param name="folder">The absolute folder, or null.</param>
    /// <param name="max">The most files to return.</param>
    /// <returns>Absolute file paths, ordered by name.</returns>
    internal static IReadOnlyList<string> ListPdfs(string? folder, int max)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory
                .EnumerateFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreachable network share is a normal Tuesday, not a reason to fail the solve.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Reduces a typed folder name to a single safe folder name.
    /// </summary>
    /// <param name="folderName">The typed name.</param>
    /// <returns>The folder name used on disk.</returns>
    internal static string FolderKey(string? folderName)
    {
        string key = Sanitize(folderName ?? string.Empty);
        return key.Length == 0 ? UnnamedKey : key;
    }

    /// <summary>
    /// Determines whether a typed value should be treated as a filesystem location rather than a
    /// folder name. Deliberately stricter than <see cref="Path.IsPathRooted(string)"/>, which calls
    /// a leading slash rooted and would silently reinterpret a name somebody typed with one.
    /// </summary>
    /// <param name="value">The typed value.</param>
    /// <returns>True when the value looks like a real path.</returns>
    private static bool IsRootedPath(string value) =>
        (value.Length >= 2 && value[1] == ':') ||
        value.StartsWith(@"\\", StringComparison.Ordinal) ||
        value.StartsWith("//", StringComparison.Ordinal) ||
        value.StartsWith('/');

    /// <summary>
    /// Returns <c>Files/PDFS</c> beside the executing assembly.
    /// </summary>
    /// <returns>The PDF library root.</returns>
    private static string PdfsRoot()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return assemblyDir is null ? "PDFS" : Path.Combine(assemblyDir, "Files", "PDFS");
    }

    /// <summary>
    /// Replaces anything that cannot appear in a file name — separators included, which is what
    /// makes this a containment guard — with a dash, then trims dots and dashes off the ends so
    /// <c>..</c> cannot address a parent.
    /// </summary>
    /// <param name="value">The typed name.</param>
    /// <returns>The sanitized name.</returns>
    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            builder.Append(invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c);
        }

        return builder.ToString().Trim('-', '.');
    }
}
