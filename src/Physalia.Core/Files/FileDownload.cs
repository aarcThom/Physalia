// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.Naming;
using Physalia.Core.Packaging;

namespace Physalia.Core.Files;

/// <summary>
/// Fetches a file into a project folder.
///
/// <para><b>The model naming a URL is not the risk; writing to disk is.</b> Physalia already lets a
/// model fetch any URL it likes through <c>read_url</c>, so reach is nothing new. What is new is that
/// the response body stays on the machine afterwards — so every guard here is about the DESTINATION,
/// not the source:</para>
///
/// <list type="bullet">
/// <item><description>The file name is reduced to a single segment and joined to the project folder,
/// and the RESOLVED path is checked back against that folder. Same posture as
/// <c>ApiRequest.ComposeUri</c>: a name is checked by where it ends up, never by how it looks.</description></item>
/// <item><description><c>http</c> and <c>https</c> only, re-checked on the FINAL address after
/// redirects — an allowed URL that redirects to something else is the point of checking twice.</description></item>
/// <item><description>The byte budget is enforced while streaming, not from <c>Content-Length</c>,
/// which is a claim. A declared length that is already too big is refused before a byte is fetched,
/// as a courtesy rather than as the control.</description></item>
/// <item><description>Written to a temp file and moved into place, so an interrupted 400MB download
/// never leaves a truncated file that looks complete.</description></item>
/// <item><description>An existing file of the same size is left alone and reported. A model that
/// retries would otherwise re-fetch half a gigabyte to produce the file it already had.</description></item>
/// </list>
///
/// <para>What comes back says what actually landed — byte count, content type, and the format read
/// out of the leading bytes. That last one exists because an open-data portal answering a missing
/// tile with a 200 and an HTML error page is indistinguishable from success by every other measure.</para>
/// </summary>
public static class FileDownload
{
    /// <summary>
    /// Fetches one file.
    /// </summary>
    /// <param name="url">The absolute http(s) URL to fetch.</param>
    /// <param name="destinationFolder">The project folder to write into.</param>
    /// <param name="fileName">
    /// What to call it, or null to take a name from the URL. Reduced to a single safe file name
    /// either way.
    /// </param>
    /// <param name="maxBytes">The byte budget for this download.</param>
    /// <param name="overwrite">True to replace an existing file of a different size.</param>
    /// <param name="client">The HTTP client to fetch with.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What landed, or why nothing did.</returns>
    public static async Task<Result<DownloadOutcome, string>> FetchAsync(
        string url,
        string destinationFolder,
        string? fileName,
        long maxBytes,
        bool overwrite,
        HttpClient client,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            return Fail("No project folder is configured, so there is nowhere to save this file.");
        }

        if (!TryParseHttpUrl(url, out Uri? source, out string urlProblem))
        {
            return Fail(urlProblem);
        }

        string name = ChooseFileName(fileName, source);
        if (!TryResolveTarget(destinationFolder, name, out string target, out string targetProblem))
        {
            return Fail(targetProblem);
        }

        try
        {
            Directory.CreateDirectory(destinationFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("The project folder could not be created: " + ex.Message);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return Fail($"The server answered {(int)response.StatusCode} {response.ReasonPhrase} for {source}.");
        }

        // Redirects are followed by the handler, so the address that actually served this may not be
        // the one that was checked. Check the one that answered.
        Uri? served = response.RequestMessage?.RequestUri;
        if (served is not null && !IsHttp(served))
        {
            return Fail($"{source} redirected to {served.Scheme}, which is not allowed.");
        }

        long? declared = response.Content.Headers.ContentLength;
        if (declared is > 0 && declared > maxBytes)
        {
            return Fail(
                $"That file is {Describe(declared.Value)}, past the {Describe(maxBytes)} limit for this node. "
                + "Raise Max Download on the Download File component if it should be fetched.");
        }

        string? contentType = response.Content.Headers.ContentType?.MediaType;
        string temp = target + ".downloading";

        // A file already here of the same size is the same file: a retry should not spend the
        // bandwidth again. A DIFFERENT size means it changed, and that needs permission.
        if (File.Exists(target))
        {
            long existing = new FileInfo(target).Length;
            if (declared is { } size && existing == size)
            {
                return new Result<DownloadOutcome, string>.Ok(
                    new DownloadOutcome(target, name, existing, contentType, true, null));
            }

            if (!overwrite)
            {
                return Fail(
                    $"\"{name}\" is already in the project folder and is a different size. "
                    + "Call again with overwrite true to replace it, or use a different file_name.");
            }
        }

        long written;
        try
        {
            using (Stream body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            using (FileStream destination = File.Create(temp))
            {
                written = await CopyBoundedAsync(body, destination, maxBytes, ct).ConfigureAwait(false);
            }

            if (written < 0)
            {
                TryDelete(temp);
                return Fail(
                    $"The download passed the {Describe(maxBytes)} limit for this node and was stopped. "
                    + "Raise Max Download on the Download File component if it should be fetched.");
            }

            File.Move(temp, target, true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            return Fail("The download was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            TryDelete(temp);
            return Fail("The download failed: " + ex.Message);
        }

        // The bytes say what the file is; the extension only says what it was called. A portal that
        // answers a missing tile with an error page produces a perfectly successful download of the
        // wrong thing, and nothing else here would notice.
        string? warning = FileSniff.IsUnexpectedHtml(target, contentType)
            ? "WARNING: the server returned an HTML page, not the file this name suggests. It is most "
              + "likely an error or login page. Read it before using it, and check the URL."
            : null;

        FileNature nature = FileSniff.Describe(target);

        DownloadLedger.Record(destinationFolder, new PhyDownloadRecord(source.ToString(), name, written));

        return new Result<DownloadOutcome, string>.Ok(
            new DownloadOutcome(target, name, written, contentType, false, warning) { Format = nature.Format });
    }

    /// <summary>
    /// Unpacks a downloaded archive beside itself, within the usual limits.
    /// </summary>
    /// <param name="archivePath">The archive to unpack.</param>
    /// <param name="projectFolder">The project folder, which the output must stay inside.</param>
    /// <param name="limits">Entry and byte bounds.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What was written, or why nothing was.</returns>
    public static Result<ZipExtractSummary, string> Extract(
        string archivePath,
        string projectFolder,
        ZipExtractLimits? limits = null,
        CancellationToken ct = default)
    {
        if (!ProjectPaths.IsContained(projectFolder, archivePath))
        {
            return new Result<ZipExtractSummary, string>.Err(
                "That archive is not in the project folder.");
        }

        // Into a folder named after the archive, so two downloads cannot overwrite each other's
        // contents and it is obvious afterwards where a given file came from.
        string destination = Path.Combine(
            projectFolder,
            ProjectPaths.FolderKey(Path.GetFileNameWithoutExtension(archivePath)));

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            return ZipSafety.ExtractTo(archive, destination, limits ?? ZipExtractLimits.Default, null, ct);
        }
        catch (InvalidDataException)
        {
            return new Result<ZipExtractSummary, string>.Err(
                "That file is not a zip archive, so there is nothing to unpack.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<ZipExtractSummary, string>.Err("The archive could not be unpacked: " + ex.Message);
        }
    }

    /// <summary>
    /// Determines whether a file name looks like a zip archive, so extraction can be offered.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>True when the leading bytes are a zip's.</returns>
    public static bool IsArchive(string path) =>
        string.Equals(FileSniff.Describe(path).Format, "ZIP archive (or a zip-based format such as .docx or .xlsx)", StringComparison.Ordinal);

    /// <summary>
    /// Renders a byte count for a person or a model to read.
    /// </summary>
    /// <param name="bytes">The count.</param>
    /// <returns>A short size.</returns>
    public static string Describe(long bytes) => bytes switch
    {
        >= 1_000_000_000 => (bytes / 1_000_000_000d).ToString("0.#") + " GB",
        >= 1_000_000 => (bytes / 1_000_000d).ToString("0.#") + " MB",
        >= 1_000 => (bytes / 1_000d).ToString("0.#") + " KB",
        _ => bytes + " bytes",
    };

    private static bool TryParseHttpUrl(string url, out Uri? parsed, out string problem)
    {
        parsed = null;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            problem = "A url is required.";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? candidate))
        {
            problem = $"\"{url}\" is not an absolute URL.";
            return false;
        }

        if (!IsHttp(candidate))
        {
            problem = $"Only http and https URLs can be downloaded; \"{candidate.Scheme}\" is not one.";
            return false;
        }

        parsed = candidate;
        return true;
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    // A model-supplied name, or the last path segment of the URL, reduced to one safe file name. The
    // extension is preserved when there is one, because it is what tells a downstream reader (and
    // Rhino's importer) what the file is.
    private static string ChooseFileName(string? requested, Uri source)
    {
        string candidate = requested?.Trim() ?? string.Empty;

        if (candidate.Length == 0)
        {
            try
            {
                candidate = Path.GetFileName(Uri.UnescapeDataString(source.AbsolutePath));
            }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException)
            {
                candidate = string.Empty;
            }
        }

        if (candidate.Length == 0)
        {
            candidate = "download";
        }

        string extension = Path.GetExtension(candidate);
        string stem = ProjectPaths.FolderKey(Path.GetFileNameWithoutExtension(candidate));
        string suffix = new string(extension.Where(c => char.IsLetterOrDigit(c) || c == '.').ToArray());

        return stem + suffix;
    }

    private static bool TryResolveTarget(string folder, string name, out string target, out string problem)
    {
        target = string.Empty;
        problem = string.Empty;

        try
        {
            target = Path.GetFullPath(Path.Combine(folder, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = $"\"{name}\" is not a usable file name.";
            return false;
        }

        if (!ProjectPaths.IsContained(folder, target))
        {
            problem = $"\"{name}\" would be saved outside the project folder.";
            return false;
        }

        return true;
    }

    // Returns the bytes copied, or -1 when the budget was passed. Counting here rather than trusting
    // Content-Length is the whole control: a header is something the server said.
    private static async Task<long> CopyBoundedAsync(Stream source, Stream destination, long budget, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read <= 0)
            {
                return total;
            }

            total += read;
            if (total > budget)
            {
                return -1;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
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
            // A leftover .downloading file is untidy, not harmful.
        }
    }

    private static Result<DownloadOutcome, string> Fail(string message) =>
        new Result<DownloadOutcome, string>.Err(message);
}

/// <summary>
/// What a download produced.
/// </summary>
/// <param name="Path">Where the file landed.</param>
/// <param name="FileName">Its name inside the project folder.</param>
/// <param name="Bytes">How big it is.</param>
/// <param name="ContentType">What the server said it was.</param>
/// <param name="AlreadyPresent">True when nothing was fetched because the file was already there.</param>
/// <param name="Warning">Something the model must be told about what landed, or null.</param>
public sealed record DownloadOutcome(
    string Path,
    string FileName,
    long Bytes,
    string? ContentType,
    bool AlreadyPresent,
    string? Warning)
{
    /// <summary>
    /// Gets the format read out of the file's leading bytes, or null when it is ordinary text.
    /// </summary>
    public string? Format { get; init; }
}
