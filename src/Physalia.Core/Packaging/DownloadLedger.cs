// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Packaging;

/// <summary>
/// What a pipeline fetched, recorded beside the files themselves in <c>downloads.json</c>.
///
/// <para><b>It exists so a package can carry knowledge instead of bytes.</b> A LiDAR tile is 400MB
/// and re-fetchable; the URL it came from is two hundred bytes and is the part that is actually hard
/// to reproduce. Bundling the tile makes a workflow that gets emailed exactly once. So a package
/// carries the ledger in its manifest, and files the ledger accounts for are left out — while a
/// site survey somebody dropped in by hand, which nothing can re-fetch, is bundled normally. The
/// ledger is what tells those two apart, and there is no other way to tell them apart.</para>
///
/// <para>Plain JSON, in the project folder rather than under <c>%LOCALAPPDATA%</c>: it describes that
/// folder's contents, it travels with it, and it holds no secrets — a URL the model could fetch again
/// tomorrow. A missing or unreadable ledger means "nothing is known to be re-fetchable", which
/// bundles more than necessary and loses nothing.</para>
/// </summary>
public static class DownloadLedger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads a project folder's ledger.
    /// </summary>
    /// <param name="projectFolder">The folder to read from.</param>
    /// <returns>What was recorded, or an empty list.</returns>
    public static IReadOnlyList<PhyDownloadRecord> Read(string? projectFolder)
    {
        string? path = PathIn(projectFolder);
        if (path is null || !File.Exists(path))
        {
            return Array.Empty<PhyDownloadRecord>();
        }

        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), SerializerOptions);
            return document?.Downloads is { Count: > 0 } records
                ? records.Where(r => !string.IsNullOrWhiteSpace(r.File) && !string.IsNullOrWhiteSpace(r.Url)).ToList()
                : Array.Empty<PhyDownloadRecord>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<PhyDownloadRecord>();
        }
    }

    /// <summary>
    /// Records one download, replacing any earlier entry for the same file.
    /// </summary>
    /// <param name="projectFolder">The folder the file was saved into.</param>
    /// <param name="record">What was fetched.</param>
    public static void Record(string? projectFolder, PhyDownloadRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        string? path = PathIn(projectFolder);
        if (path is null)
        {
            return;
        }

        List<PhyDownloadRecord> entries = Read(projectFolder)
            .Where(r => !SameFile(r.File, record.File))
            .ToList();

        entries.Add(record);
        Write(path, entries);
    }

    /// <summary>
    /// Forgets a file, so a download that was deleted stops being reported as re-fetchable.
    /// </summary>
    /// <param name="projectFolder">The project folder.</param>
    /// <param name="fileName">The file, relative to that folder.</param>
    public static void Forget(string? projectFolder, string fileName)
    {
        string? path = PathIn(projectFolder);
        if (path is null || !File.Exists(path))
        {
            return;
        }

        List<PhyDownloadRecord> entries = Read(projectFolder)
            .Where(r => !SameFile(r.File, fileName))
            .ToList();

        Write(path, entries);
    }

    /// <summary>
    /// Determines whether a file in the project folder is one the ledger accounts for, and so can be
    /// left out of a package and fetched again at the other end.
    /// </summary>
    /// <param name="ledger">The records, already read.</param>
    /// <param name="relativeName">The file's name relative to the project folder.</param>
    /// <returns>True when the file is re-fetchable.</returns>
    public static bool IsRefetchable(IReadOnlyList<PhyDownloadRecord> ledger, string relativeName) =>
        ledger.Any(record => SameFile(record.File, relativeName));

    /// <summary>
    /// The ledger's own path inside a project folder.
    /// </summary>
    /// <param name="projectFolder">The project folder, which may be null.</param>
    /// <returns>The ledger path, or null when no folder was given.</returns>
    public static string? PathIn(string? projectFolder) =>
        string.IsNullOrWhiteSpace(projectFolder)
            ? null
            : Path.Combine(projectFolder, Naming.ProjectPaths.DownloadLedgerFile);

    private static bool SameFile(string a, string b) =>
        string.Equals(
            a?.Replace('\\', '/').TrimStart('/'),
            b?.Replace('\\', '/').TrimStart('/'),
            StringComparison.OrdinalIgnoreCase);

    private static void Write(string path, List<PhyDownloadRecord> entries)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var document = new Document { Downloads = entries };
            File.WriteAllText(path, JsonSerializer.Serialize(document, SerializerOptions), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A ledger that cannot be written costs a package some extra megabytes, not the download
            // that just succeeded. Nothing here is worth failing a tool call over.
        }
    }

    private sealed class Document
    {
        public List<PhyDownloadRecord> Downloads { get; set; } = new();
    }
}
