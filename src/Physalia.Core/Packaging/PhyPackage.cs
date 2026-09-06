// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Physalia.Core.Common;

namespace Physalia.Core.Packaging;

/// <summary>
/// Reads and writes <c>.phy</c> — a harness and everything that travels with it, in one file.
///
/// <para>It is an ordinary zip:</para>
/// <code>
/// manifest.json   the harness's name, description and chat text, plus the download ledger
/// harness.gh      the sub-document archive, byte-for-byte what a .gh preset holds
/// files/          the project-files folder's contents
/// </code>
///
/// <para><b>The inner document is left untouched on purpose.</b> It is exactly the bytes
/// <c>PresetLibrary</c> already wrote as a <c>.gh</c>, so a <c>.phy</c> can be renamed to
/// <c>.zip</c>, opened by hand, and the definition inside dragged into Grasshopper. A format nobody
/// can get their work back out of is not a format a firm should standardise on, and that property is
/// worth more than any saving from a bespoke layout.</para>
///
/// <para><b>Format is decided by content, not by extension.</b> A file starting <c>PK</c> is a
/// package; anything else is handed back for the legacy path to read as a plain <c>.gh</c>. The
/// shipped presets are still <c>.gh</c>, users rename files, and a mail client will happily change an
/// extension on the way through — none of that should decide how a file is parsed.</para>
///
/// <para>Nothing per-machine goes in. Credentials, provider activations, MCP servers and API
/// endpoints are all deliberately absent: they belong to the machine, and one of them is secrets. The
/// API catalog a pipeline actually needs already rides on its node, inside the document.</para>
/// </summary>
public static class PhyPackage
{
    /// <summary>
    /// The package file extension, including the dot.
    /// </summary>
    public const string Extension = ".phy";

    /// <summary>
    /// The manifest entry's name inside the package.
    /// </summary>
    public const string ManifestEntry = "manifest.json";

    /// <summary>
    /// The Grasshopper document entry's name inside the package.
    /// </summary>
    public const string DocumentEntry = "harness.gh";

    /// <summary>
    /// The prefix every project-file entry sits under.
    /// </summary>
    public const string FilesPrefix = "files/";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Determines whether a file is a package, by looking at its first two bytes.
    /// </summary>
    /// <param name="path">The file to test.</param>
    /// <returns>True when the file is a zip and so may be a package.</returns>
    public static bool IsPackage(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return stream.ReadByte() == 'P' && stream.ReadByte() == 'K';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads just the manifest. Cheap enough for the preset gallery to call on every file it lists —
    /// it opens the archive's directory and one small entry, and never touches the document.
    /// </summary>
    /// <param name="path">The package to read.</param>
    /// <returns>The manifest, or why it could not be read.</returns>
    public static Result<PhyManifest, string> ReadManifest(string path)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            return ReadManifest(archive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new Result<PhyManifest, string>.Err("The package could not be opened: " + ex.Message);
        }
    }

    /// <summary>
    /// Reads a package's manifest and Grasshopper document, leaving the project files where they are.
    ///
    /// <para>The files are not extracted here because the caller cannot know where they go until it
    /// has the manifest: the destination is the project folder of the harness the document is about to
    /// become, and its name is in the manifest it is being handed.</para>
    /// </summary>
    /// <param name="path">The package to read.</param>
    /// <returns>The manifest and document bytes, or why the package could not be read.</returns>
    public static Result<PhyPackageContents, string> Read(string path)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);

            Result<PhyManifest, string> manifest = ReadManifest(archive);
            if (!manifest.IsOk(out PhyManifest? read, out string? error))
            {
                return new Result<PhyPackageContents, string>.Err(error);
            }

            ZipArchiveEntry? document = archive.GetEntry(DocumentEntry);
            if (document is null)
            {
                return new Result<PhyPackageContents, string>.Err(
                    "The package holds no " + DocumentEntry + ", so there is no pipeline in it.");
            }

            using Stream stream = document.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            return new Result<PhyPackageContents, string>.Ok(
                new PhyPackageContents(read, buffer.ToArray(), CountFiles(archive)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new Result<PhyPackageContents, string>.Err("The package could not be read: " + ex.Message);
        }
    }

    /// <summary>
    /// Extracts a package's project files into a folder.
    ///
    /// <para>Runs through <see cref="ZipSafety"/>, because a package is a file somebody else wrote:
    /// every entry is resolved back against the destination before it is opened, and the bytes are
    /// counted as they land rather than taken from the header.</para>
    /// </summary>
    /// <param name="path">The package to read.</param>
    /// <param name="destinationRoot">The project folder to write into.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What was written, or why extraction stopped.</returns>
    public static Result<ZipExtractSummary, string> ExtractFiles(
        string path,
        string destinationRoot,
        CancellationToken ct = default)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);

            // Only the payload half, and with its prefix dropped: the manifest and the document
            // describe the package rather than belonging to the project, so neither may be written
            // into the project folder, and files/site.las must land as site.las.
            return ZipSafety.ExtractTo(
                archive,
                destinationRoot,
                ZipExtractLimits.Default,
                entry => entry.FullName.StartsWith(FilesPrefix, StringComparison.OrdinalIgnoreCase)
                    ? entry.FullName.Substring(FilesPrefix.Length)
                    : null,
                ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new Result<ZipExtractSummary, string>.Err("The package could not be unpacked: " + ex.Message);
        }
    }

    /// <summary>
    /// Writes a package.
    /// </summary>
    /// <param name="path">Where to write it; an existing file is replaced.</param>
    /// <param name="manifest">The harness's metadata.</param>
    /// <param name="documentBytes">The harness sub-document, archived as a <c>.gh</c> would be.</param>
    /// <param name="files">
    /// Project files to carry, as (relative name, absolute source path) pairs. A file that has gone
    /// missing since it was listed is skipped rather than failing the write — the package is still
    /// worth having.
    /// </param>
    /// <returns>The bytes written to disk, or why the write failed.</returns>
    public static Result<long, string> Write(
        string path,
        PhyManifest manifest,
        byte[] documentBytes,
        IReadOnlyList<PhyPackageFile>? files = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(documentBytes);

        // Written to a temp file beside the target and moved into place, so an interrupted write
        // cannot leave a truncated package sitting where a working one used to be.
        string temp = path + ".writing";

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (FileStream stream = File.Create(temp))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteText(archive, ManifestEntry, JsonSerializer.Serialize(manifest, SerializerOptions));

                ZipArchiveEntry document = archive.CreateEntry(DocumentEntry, CompressionLevel.Optimal);
                using (Stream entry = document.Open())
                {
                    entry.Write(documentBytes, 0, documentBytes.Length);
                }

                foreach (PhyPackageFile file in files ?? Array.Empty<PhyPackageFile>())
                {
                    AddFile(archive, file);
                }
            }

            var written = new FileInfo(temp);
            long bytes = written.Length;

            File.Move(temp, path, true);
            return new Result<long, string>.Ok(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temp);
            return new Result<long, string>.Err("The package could not be written: " + ex.Message);
        }
    }

    private static Result<PhyManifest, string> ReadManifest(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry(ManifestEntry);
        if (entry is null)
        {
            return new Result<PhyManifest, string>.Err(
                "The file is a zip but holds no " + ManifestEntry + ", so it is not a Physalia package.");
        }

        try
        {
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            PhyManifest? manifest = JsonSerializer.Deserialize<PhyManifest>(reader.ReadToEnd(), SerializerOptions);

            if (manifest is null)
            {
                return new Result<PhyManifest, string>.Err("The package's manifest is empty.");
            }

            // Refused rather than guessed at. A future layout may put things somewhere this build
            // will not look, and a half-understood import is worse than a clear refusal.
            if (manifest.FormatVersion > PhyManifest.CurrentFormatVersion)
            {
                return new Result<PhyManifest, string>.Err(
                    "This package was written by a newer Physalia (format " + manifest.FormatVersion
                    + "; this build reads " + PhyManifest.CurrentFormatVersion + "). Update Physalia to open it.");
            }

            return new Result<PhyManifest, string>.Ok(manifest);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            return new Result<PhyManifest, string>.Err("The package's manifest could not be read: " + ex.Message);
        }
    }

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void AddFile(ZipArchive archive, PhyPackageFile file)
    {
        if (!File.Exists(file.SourcePath))
        {
            return;
        }

        try
        {
            string name = FilesPrefix + file.RelativeName.Replace('\\', '/').TrimStart('/');
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);

            using Stream target = entry.Open();
            using FileStream source = File.OpenRead(file.SourcePath);
            source.CopyTo(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // One unreadable file — open in another application, or on a share that just went away —
            // is not worth losing the whole package over.
        }
    }

    private static int CountFiles(ZipArchive archive)
    {
        int count = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Name.Length > 0
                && entry.FullName.StartsWith(FilesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do about a temp file that will not delete.
        }
    }
}

/// <summary>
/// What a package holds, short of its project files.
/// </summary>
/// <param name="Manifest">The harness's metadata.</param>
/// <param name="DocumentBytes">The sub-document archive, ready to be read as a <c>.gh</c> would be.</param>
/// <param name="FileCount">How many project files the package carries.</param>
public sealed record PhyPackageContents(PhyManifest Manifest, byte[] DocumentBytes, int FileCount);

/// <summary>
/// One project file on its way into a package.
/// </summary>
/// <param name="RelativeName">Its name relative to the project folder.</param>
/// <param name="SourcePath">Where to read it from now.</param>
public sealed record PhyPackageFile(string RelativeName, string SourcePath);
