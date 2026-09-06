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
/// Lists the PDFs in a folder, bounded.
///
/// <para>All that is left of what used to be a whole location scheme. PDFs had a library of their
/// own under <c>Files/PDFS</c>, with its own name-or-path spelling rules and its own copy of the
/// folder-name sanitizer; they are project material like anything else, so they now live in
/// <c>&lt;project folder&gt;/PDF</c> and the rules live once, in <c>ProjectPaths</c>. What remains
/// here is the one thing that was never about locations: enumerating a folder without letting a
/// network share that has gone away take a solve down with it.</para>
/// </summary>
internal static class PdfLocations
{
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



}
