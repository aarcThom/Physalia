// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Physalia.Core.Common;

namespace Physalia.Core.Packaging;

/// <summary>
/// Bounds and containment for extracting a zip that came from somewhere else — a <c>.phy</c> a
/// colleague emailed, or an archive a model asked to be downloaded.
///
/// <para>Both callers face the same two hazards, and neither can be handled by trusting the archive.
/// An entry name is text somebody else wrote, so <c>../../../Startup/run.bat</c> writes wherever it
/// likes unless the RESOLVED path is checked back against the destination root — the same posture
/// <c>ApiRequest.ComposeUri</c> takes towards a model-supplied path. And a declared length is just a
/// number in a header, so a zip bomb is 10KB in and 100GB out unless the bytes are counted as they
/// are written rather than believed in advance.</para>
///
/// <para>Extraction stops at the first refusal and reports it. A partial extraction is left where it
/// is: deleting files afterwards is a second destructive act driven by a path that has already proved
/// it cannot be trusted, and the caller knows where it was pointed.</para>
/// </summary>
public static class ZipSafety
{
    /// <summary>
    /// Resolves one archive entry to an absolute path underneath a destination root, refusing
    /// anything that would land outside it.
    ///
    /// <para>Checks the RESOLVED path rather than scanning the entry name for <c>..</c>: a name can
    /// climb by many routes — a leading separator, a drive letter, a UNC prefix, mixed separators,
    /// more <c>..</c> segments than a naive counter expects — and only one thing about it is worth
    /// reasoning over, which is where the path actually ends up.</para>
    /// </summary>
    /// <param name="destinationRoot">The folder everything must land inside.</param>
    /// <param name="entryName">The archive entry's full name.</param>
    /// <param name="fullPath">The resolved absolute path.</param>
    /// <param name="error">Why the entry was refused.</param>
    /// <returns>True when the entry resolves safely.</returns>
    public static bool TryResolveEntryPath(
        string destinationRoot,
        string entryName,
        out string fullPath,
        out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            error = "No destination folder was given.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entryName))
        {
            error = "The archive holds an entry with no name.";
            return false;
        }

        // Zip stores forward slashes by convention, but plenty of writers emit backslashes and on
        // Linux a backslash is a legal file-name character — normalising both is what keeps this
        // check identical on every platform.
        string relative = entryName.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0)
        {
            error = "\"" + entryName + "\" names no file.";
            return false;
        }

        string root;
        string candidate;
        try
        {
            root = Path.GetFullPath(destinationRoot);
            candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "\"" + entryName + "\" is not a usable file name.";
            return false;
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "\"" + entryName + "\" points outside the destination folder.";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// Extracts an archive into a destination folder, within the given limits.
    /// </summary>
    /// <param name="archive">The open archive.</param>
    /// <param name="destinationRoot">The folder to extract into; created if missing.</param>
    /// <param name="limits">Entry-count and byte bounds.</param>
    /// <param name="nameFor">
    /// Optional mapping from an archive entry to the path it should be written to, relative to the
    /// destination — return null to skip the entry entirely. It selects and renames in one step,
    /// which is what the package reader needs: it takes only the <c>files/</c> half of a
    /// <c>.phy</c> AND drops that prefix, so <c>files/site.las</c> lands as <c>site.las</c>. Whatever
    /// it returns is still resolved and contained like any other entry name — a mapping function is
    /// not a way around the guard.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What was written, or why extraction stopped.</returns>
    public static Result<ZipExtractSummary, string> ExtractTo(
        ZipArchive archive,
        string destinationRoot,
        ZipExtractLimits limits,
        Func<ZipArchiveEntry, string?>? nameFor = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(limits);

        var written = new List<string>();
        long total = 0;

        try
        {
            Directory.CreateDirectory(destinationRoot);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();

                // A directory entry is a name ending in a separator, with no content. Nothing to
                // write: the folders are created by the files that need them.
                if (entry.Name.Length == 0)
                {
                    continue;
                }

                string? relative = nameFor is null ? entry.FullName : nameFor(entry);
                if (relative is null)
                {
                    continue;
                }

                if (written.Count >= limits.MaxEntries)
                {
                    return new Result<ZipExtractSummary, string>.Err(
                        "The archive holds more than " + limits.MaxEntries
                        + " files, which is past what Physalia will unpack in one go.");
                }

                if (!TryResolveEntryPath(destinationRoot, relative, out string target, out string refusal))
                {
                    return new Result<ZipExtractSummary, string>.Err(refusal);
                }

                string? dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                long budget = Math.Min(limits.MaxEntryBytes, limits.MaxTotalBytes - total);
                if (!TryCopyBounded(entry, target, budget, out long bytes))
                {
                    return new Result<ZipExtractSummary, string>.Err(
                        "\"" + entry.FullName + "\" expands past the " + Describe(limits.MaxTotalBytes)
                        + " unpack limit. The archive may be misreporting its size.");
                }

                total += bytes;
                written.Add(target);
            }

            return new Result<ZipExtractSummary, string>.Ok(new ZipExtractSummary(written, total));
        }
        catch (OperationCanceledException)
        {
            return new Result<ZipExtractSummary, string>.Err("Unpacking was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new Result<ZipExtractSummary, string>.Err("The archive could not be unpacked: " + ex.Message);
        }
    }

    // Copies one entry, counting the bytes actually written rather than trusting entry.Length — the
    // declared size lives in the archive header and a bomb simply lies about it. Stops the moment the
    // budget is passed, so an over-long entry costs the budget rather than the disk.
    private static bool TryCopyBounded(ZipArchiveEntry entry, string target, long budget, out long written)
    {
        written = 0;
        if (budget <= 0)
        {
            return false;
        }

        var buffer = new byte[81920];
        using Stream source = entry.Open();
        using FileStream destination = File.Create(target);

        while (true)
        {
            int read = source.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return true;
            }

            written += read;
            if (written > budget)
            {
                return false;
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static string Describe(long bytes) =>
        bytes >= 1_000_000_000
            ? (bytes / 1_000_000_000d).ToString("0.#") + " GB"
            : (bytes / 1_000_000d).ToString("0.#") + " MB";
}

/// <summary>
/// Bounds on one extraction. The defaults are sized for a harness package holding real project files
/// — a survey, a point cloud — while still refusing an archive that could fill a disk.
/// </summary>
/// <param name="MaxEntries">The most files to write.</param>
/// <param name="MaxTotalBytes">The most bytes to write across the whole archive.</param>
/// <param name="MaxEntryBytes">The most bytes any single entry may expand to.</param>
public sealed record ZipExtractLimits(
    int MaxEntries = 5000,
    long MaxTotalBytes = 4_000_000_000,
    long MaxEntryBytes = 2_000_000_000)
{
    /// <summary>
    /// Gets the default limits.
    /// </summary>
    public static ZipExtractLimits Default { get; } = new();
}

/// <summary>
/// What one extraction produced.
/// </summary>
/// <param name="Files">Absolute paths written, in archive order.</param>
/// <param name="TotalBytes">Bytes written across all of them.</param>
public sealed record ZipExtractSummary(IReadOnlyList<string> Files, long TotalBytes);
