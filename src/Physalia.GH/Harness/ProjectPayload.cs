// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Physalia.Core.Naming;
using Physalia.Core.Packaging;

namespace Physalia.GH.Harness;

/// <summary>
/// Decides what of a harness's project folder travels inside its <c>.phy</c>, and what is left to be
/// fetched again at the other end.
///
/// <para>The split is the whole reason packaging a workflow is affordable. Everything in the folder
/// is carried EXCEPT files the download ledger accounts for: those have a URL, so the package records
/// where they came from and the receiving end gets them on demand. A 400MB LiDAR tile therefore costs
/// a package about two hundred bytes, while the site survey somebody dragged in — which nothing can
/// re-fetch — is carried in full.</para>
///
/// <para>The ledger file itself is carried, because it is the knowledge: without it the other end has
/// a manifest of URLs and no idea which local file each one was meant to become.</para>
/// </summary>
internal static class ProjectPayload
{
    /// <summary>
    /// Works out what a package should carry from a project folder.
    /// </summary>
    /// <param name="projectFolder">The harness's project folder; may not exist.</param>
    /// <returns>The files to bundle, the downloads to record, and what it all weighs.</returns>
    internal static ProjectPayloadPlan Plan(string? projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
        {
            return ProjectPayloadPlan.Empty;
        }

        IReadOnlyList<PhyDownloadRecord> ledger = DownloadLedger.Read(projectFolder);

        var files = new List<PhyPackageFile>();
        var refetched = new List<PhyDownloadRecord>();
        long bundled = 0;
        long deferred = 0;

        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProjectPayloadPlan.Empty;
        }

        foreach (string path in found)
        {
            string relative = Relative(projectFolder, path);
            if (relative.Length == 0)
            {
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            PhyDownloadRecord? record = ledger.FirstOrDefault(r => Same(r.File, relative));
            if (record is not null)
            {
                // Re-fetchable: the URL goes in the manifest, the bytes stay here.
                refetched.Add(record with { Bytes = size });
                deferred += size;
                continue;
            }

            files.Add(new PhyPackageFile(relative, path));
            bundled += size;
        }

        // A ledger entry whose file has since been deleted is still worth carrying: the URL is the
        // knowledge, and the other end can decide whether it wants the file.
        foreach (PhyDownloadRecord record in ledger)
        {
            if (!refetched.Any(r => Same(r.File, record.File)))
            {
                refetched.Add(record);
            }
        }

        return new ProjectPayloadPlan(files, refetched, bundled, deferred);
    }

    private static string Relative(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(root, path).Replace('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private static bool Same(string a, string b) =>
        string.Equals(
            a?.Replace('\\', '/').TrimStart('/'),
            b?.Replace('\\', '/').TrimStart('/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Renders a byte count the way a person reading a dialog wants it.
    /// </summary>
    /// <param name="bytes">The count.</param>
    /// <returns>A short human-readable size.</returns>
    internal static string Describe(long bytes) => bytes switch
    {
        >= 1_000_000_000 => (bytes / 1_000_000_000d).ToString("0.#") + " GB",
        >= 1_000_000 => (bytes / 1_000_000d).ToString("0.#") + " MB",
        >= 1_000 => (bytes / 1_000d).ToString("0.#") + " KB",
        _ => bytes + " bytes",
    };
}

/// <summary>
/// What a package will carry from a project folder.
/// </summary>
/// <param name="Files">Files to bundle.</param>
/// <param name="Downloads">Downloads to record rather than carry.</param>
/// <param name="BundledBytes">What the bundled files weigh.</param>
/// <param name="DeferredBytes">What is being left out, so the user can be told what they saved.</param>
internal sealed record ProjectPayloadPlan(
    IReadOnlyList<PhyPackageFile> Files,
    IReadOnlyList<PhyDownloadRecord> Downloads,
    long BundledBytes,
    long DeferredBytes)
{
    /// <summary>
    /// Gets a plan that carries nothing.
    /// </summary>
    internal static ProjectPayloadPlan Empty { get; } =
        new(Array.Empty<PhyPackageFile>(), Array.Empty<PhyDownloadRecord>(), 0, 0);

    /// <summary>
    /// Gets a one-line summary of what the package will contain, or null when it carries nothing.
    /// </summary>
    internal string? Summary
    {
        get
        {
            var parts = new List<string>();

            if (this.Files.Count > 0)
            {
                parts.Add(this.Files.Count + " project file" + (this.Files.Count == 1 ? string.Empty : "s")
                    + " (" + ProjectPayload.Describe(this.BundledBytes) + ")");
            }

            if (this.Downloads.Count > 0)
            {
                parts.Add(this.Downloads.Count + " download" + (this.Downloads.Count == 1 ? string.Empty : "s")
                    + " recorded to fetch again (" + ProjectPayload.Describe(this.DeferredBytes) + " not carried)");
            }

            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
    }
}
