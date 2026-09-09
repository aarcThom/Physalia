// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Config;

/// <summary>
/// Pulls one release's section out of the shipped changelog, so the update notice can say what
/// changed rather than only that something did.
/// </summary>
/// <remarks>
/// <para>Deliberately the smallest possible reader: a level-two heading whose text starts with the
/// version number opens a section, and the next level-two heading closes it. No markdown library, no
/// schema, nothing to keep in step — and a changelog that does not follow the convention simply
/// yields nothing, which costs the notice its detail and nothing else.</para>
/// <para>The version in the heading may be written to any depth (<c>## 1.2</c>, <c>## 1.2.0</c>) and
/// may carry a date or a name after it (<c>## 1.2.0 — 2026-09-09</c>); it is compared as a version,
/// not as a string, because <c>1.2</c> and <c>1.2.0.0</c> are the same release.</para>
/// </remarks>
public static class ReleaseNotes
{
    /// <summary>
    /// Finds the section for a version.
    /// </summary>
    /// <param name="markdown">The whole changelog.</param>
    /// <param name="version">The version wanted, in any depth of spelling.</param>
    /// <returns>The section's body, trimmed, or null when there is no such section.</returns>
    public static string? SectionFor(string? markdown, string? version)
    {
        if (string.IsNullOrWhiteSpace(markdown) || !TryParse(version, out Version? wanted))
        {
            return null;
        }

        var body = new List<string>();
        bool inSection = false;

        foreach (string line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (IsHeading(line, out string? headingVersion))
            {
                if (inSection)
                {
                    break;
                }

                inSection = TryParse(headingVersion, out Version? found) && found == wanted;
                continue;
            }

            if (inSection)
            {
                body.Add(line);
            }
        }

        if (!inSection)
        {
            return null;
        }

        string text = string.Join("\n", body).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Reads a changelog off disk.
    /// </summary>
    /// <param name="path">The changelog file, which need not exist.</param>
    /// <param name="version">The version wanted.</param>
    /// <returns>The section, or null when the file is missing, unreadable or has no such section.</returns>
    public static string? SectionFromFile(string? path, string? version)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return File.Exists(path) ? SectionFor(File.ReadAllText(path), version) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A level-two heading only: "#" is the changelog's own title and "###" is something inside a
    // release, so neither opens or closes a section.
    private static bool IsHeading(string line, out string? version)
    {
        version = null;
        string trimmed = line.TrimStart();

        if (!trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return false;
        }

        string rest = trimmed.Substring(3).Trim().TrimStart('v', 'V');

        // Up to the first space, so a date or a release name after the number is ignored.
        int space = rest.IndexOf(' ');
        version = space < 0 ? rest : rest.Substring(0, space);
        return true;
    }

    // Compared at a fixed depth, so "1.2", "1.2.0" and "1.2.0.0" are one release rather than three:
    // Version's own equality treats an absent part as -1 and an explicit zero as 0.
    private static bool TryParse(string? value, out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim().TrimStart('v', 'V');
        if (!System.Version.TryParse(text.Contains('.') ? text : text + ".0", out Version? parsed))
        {
            return false;
        }

        version = new Version(
            parsed.Major,
            parsed.Minor,
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0));

        return true;
    }
}
