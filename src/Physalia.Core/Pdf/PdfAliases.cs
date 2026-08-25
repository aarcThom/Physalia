// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Physalia.Core.Pdf;

/// <summary>
/// Turns a file name into the short handle the model addresses a PDF by, and keeps those handles
/// unique within a session.
///
/// <para>An alias exists because the model has to name a document in a tool call, and the real
/// thing to name it by — <c>"24031 - ACME Tower - A-101 Level 03 Floor Plan (Rev C).pdf"</c> — is
/// long enough to be retyped wrong. Aliases are lowercase, single-token and hyphenated, matching
/// the shape of the <c>/&lt;alias&gt;</c> references the chat composer already uses, so one habit
/// covers both.</para>
/// </summary>
public static class PdfAliases
{
    /// <summary>
    /// The alias used when a file name sanitizes away to nothing.
    /// </summary>
    public const string Fallback = "pdf";

    // Long enough to stay recognisable, short enough that the model reliably reproduces it.
    private const int MaxLength = 48;

    /// <summary>
    /// Derives an alias from a file path, using the file name without its extension.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The sanitized alias.</returns>
    public static string FromFileName(string? path) =>
        Sanitize(string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path));

    /// <summary>
    /// Reduces arbitrary text to a single lowercase hyphenated token.
    /// </summary>
    /// <param name="value">The text to sanitize.</param>
    /// <returns>The sanitized alias, or <see cref="Fallback"/> when nothing usable remains.</returns>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Fallback;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                // Every separator — space, underscore, dot, bracket — collapses to one hyphen, so
                // the alias stays a single token however the file was named.
                builder.Append('-');
            }
        }

        string alias = builder.ToString().Trim('-');
        if (alias.Length > MaxLength)
        {
            alias = alias[..MaxLength].Trim('-');
        }

        return alias.Length == 0 ? Fallback : alias;
    }

    /// <summary>
    /// Returns an alias that does not collide with any already taken, suffixing <c>-2</c>,
    /// <c>-3</c> and so on.
    /// </summary>
    /// <param name="desired">The preferred alias.</param>
    /// <param name="taken">Aliases already in use.</param>
    /// <returns>An unused alias.</returns>
    public static string Unique(string? desired, IEnumerable<string> taken)
    {
        string root = Sanitize(desired);
        var used = new HashSet<string>(taken ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(root))
        {
            return root;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, "{0}-{1}", root, suffix);
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return root + "-" + Guid.NewGuid().ToString("N")[..6];
    }
}
