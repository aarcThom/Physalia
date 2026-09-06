// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Physalia.Core.Files;

/// <summary>
/// Works out what a file actually is by looking at its first bytes, rather than believing its name.
///
/// <para>Two callers need this and both need it for the same reason — an extension is a claim, not a
/// fact. The downloader uses it to catch the classic open-data failure, where a portal answers a
/// missing tile with a 200 and an HTML error page which then sits on disk called
/// <c>tile.las</c> and looks exactly like a success. The reader uses it to refuse a binary file with a
/// description of what it is, rather than handing the model a screenful of replacement
/// characters.</para>
/// </summary>
public static class FileSniff
{
    // Enough to cover every signature below and to judge whether the content is text.
    private const int ProbeBytes = 512;

    private static readonly (byte[] Magic, string Label)[] Signatures =
    {
        (new byte[] { 0x25, 0x50, 0x44, 0x46 }, "PDF"),
        (new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "ZIP archive (or a zip-based format such as .docx or .xlsx)"),
        (new byte[] { 0x4C, 0x41, 0x53, 0x46 }, "LAS point cloud"),
        (new byte[] { 0x4C, 0x41, 0x5A, 0x46 }, "LAZ point cloud"),
        (new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "PNG image"),
        (new byte[] { 0xFF, 0xD8, 0xFF }, "JPEG image"),
        (new byte[] { 0x47, 0x49, 0x46, 0x38 }, "GIF image"),
        (new byte[] { 0x1F, 0x8B }, "gzip archive"),
        (new byte[] { 0x37, 0x7A, 0xBC, 0xAF }, "7-Zip archive"),
        (new byte[] { 0x52, 0x61, 0x72, 0x21 }, "RAR archive"),
        (new byte[] { 0x53, 0x51, 0x4C, 0x69 }, "SQLite database"),
        (new byte[] { 0x00, 0x00, 0x00, 0x0C }, "JPEG 2000 or ISO media"),
        (new byte[] { 0x33, 0x44, 0x4D, 0x0A }, "Rhino 3DM model"),
    };

    /// <summary>
    /// Describes a file from its leading bytes.
    /// </summary>
    /// <param name="path">The file to look at.</param>
    /// <returns>What the bytes say the file is.</returns>
    public static FileNature Describe(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            var buffer = new byte[ProbeBytes];
            int read = stream.Read(buffer, 0, buffer.Length);
            return Describe(buffer.AsSpan(0, Math.Max(read, 0)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileNature(false, false, null);
        }
    }

    /// <summary>
    /// Describes a run of bytes.
    /// </summary>
    /// <param name="head">The leading bytes of a file.</param>
    /// <returns>What the bytes say the content is.</returns>
    public static FileNature Describe(ReadOnlySpan<byte> head)
    {
        if (head.Length == 0)
        {
            // An empty file reads as text: there is nothing in it that a reader would garble.
            return new FileNature(true, false, "empty");
        }

        foreach ((byte[] magic, string label) in Signatures)
        {
            if (StartsWith(head, magic))
            {
                return new FileNature(false, false, label);
            }
        }

        bool text = LooksTextual(head);
        return new FileNature(text, text && LooksHtml(head), text ? null : "binary data");
    }

    /// <summary>
    /// Determines whether a downloaded body is an HTML page when something else was expected — a
    /// portal's error page saved under a data file's name.
    /// </summary>
    /// <param name="path">The file that was written.</param>
    /// <param name="contentType">The declared content type, if any.</param>
    /// <returns>True when the file is HTML but is not named like it.</returns>
    public static bool IsUnexpectedHtml(string path, string? contentType)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".html" or ".htm" or ".xhtml")
        {
            return false;
        }

        if (contentType is { Length: > 0 } && contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Describe(path).LooksHtml;
    }

    private static bool StartsWith(ReadOnlySpan<byte> head, byte[] magic)
    {
        if (head.Length < magic.Length)
        {
            return false;
        }

        for (int i = 0; i < magic.Length; i++)
        {
            if (head[i] != magic[i])
            {
                return false;
            }
        }

        return true;
    }

    // A NUL byte is the giveaway — no text encoding a reader would want puts one in the first half
    // kilobyte — backed up by a share of bytes that are neither printable nor ordinary whitespace.
    // UTF-16 text is deliberately called binary here: it is full of NULs, and every reader in
    // Physalia decodes UTF-8.
    private static bool LooksTextual(ReadOnlySpan<byte> head)
    {
        int odd = 0;

        foreach (byte b in head)
        {
            if (b == 0)
            {
                return false;
            }

            bool printable = b >= 0x20 || b is 0x09 or 0x0A or 0x0D;
            if (!printable)
            {
                odd++;
            }
        }

        return odd * 100 / Math.Max(head.Length, 1) < 5;
    }

    private static bool LooksHtml(ReadOnlySpan<byte> head)
    {
        string text = Encoding.UTF8.GetString(head).TrimStart();
        return text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                && text.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// What a file's leading bytes say it is.
/// </summary>
/// <param name="IsText">True when the content can be read as text without garbling it.</param>
/// <param name="LooksHtml">True when the content is an HTML document.</param>
/// <param name="Format">A short name for the format, or null when it is ordinary text.</param>
public sealed record FileNature(bool IsText, bool LooksHtml, string? Format);
