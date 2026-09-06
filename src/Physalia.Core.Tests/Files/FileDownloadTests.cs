// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.Files;
using Physalia.Core.Packaging;
using Xunit;

namespace Physalia.Core.Tests.Files;

public sealed class FileDownloadTests : IDisposable
{
    private readonly string _root;

    public FileDownloadTests()
    {
        this._root = Path.Combine(Path.GetTempPath(), "dl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this._root, true);
        }
        catch (IOException)
        {
        }
    }

    // Answers every request with a canned response, so the guards can be tested without a network.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;

        internal StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => this._reply = reply;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage response = this._reply(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private static HttpClient Serving(byte[] body, string? contentType = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            if (contentType is not null)
            {
                response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            return response;
        }));
    }

    private Task<Result<DownloadOutcome, string>> Fetch(
        string url,
        HttpClient client,
        string? fileName = null,
        long maxBytes = 10_000_000,
        bool overwrite = false) =>
        FileDownload.FetchAsync(url, this._root, fileName, maxBytes, overwrite, client);

    [Fact]
    public async Task Fetch_WritesTheFileAndReportsWhatLanded()
    {
        using HttpClient client = Serving(Encoding.UTF8.GetBytes("id,name\n1,tile"), "text/csv");

        Result<DownloadOutcome, string> result = await this.Fetch("https://example.org/data/tiles.csv", client);

        Assert.True(result.IsOk(out DownloadOutcome? outcome, out string? error), error);
        Assert.Equal("tiles.csv", outcome!.FileName);
        Assert.Equal(14, outcome.Bytes);
        Assert.False(outcome.AlreadyPresent);
        Assert.True(File.Exists(outcome.Path));
        Assert.Null(outcome.Warning);
    }

    [Theory]
    [InlineData("file:///C:/Windows/system.ini")]
    [InlineData("ftp://example.org/x")]
    [InlineData("not a url")]
    public async Task Fetch_RefusesAnythingButHttp(string url)
    {
        using HttpClient client = Serving(new byte[] { 1 });

        Assert.False((await this.Fetch(url, client)).IsOk(out _, out string? error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../escape.txt")]
    [InlineData("sub/../../escape.txt")]
    public async Task Fetch_CannotBeMadeToWriteOutsideTheProjectFolder(string fileName)
    {
        using HttpClient client = Serving(Encoding.UTF8.GetBytes("x"));

        Result<DownloadOutcome, string> result = await this.Fetch("https://example.org/a", client, fileName);

        // The name is reduced to one segment, so it cannot climb — and whatever it becomes must
        // still be inside the folder.
        Assert.True(result.IsOk(out DownloadOutcome? outcome, out _));
        Assert.StartsWith(this._root, outcome!.Path, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(this._root)!, "escape.txt")));
    }

    [Fact]
    public async Task Fetch_StopsAtTheByteBudgetEvenWhenNoLengthWasDeclared()
    {
        // The budget is enforced while streaming, because Content-Length is something the server
        // said rather than something that is true.
        using HttpClient client = Serving(new byte[5000]);

        Assert.False((await this.Fetch("https://example.org/big.bin", client, maxBytes: 1000))
            .IsOk(out _, out string? error));
        Assert.Contains("limit for this node", error);
    }

    [Fact]
    public async Task Fetch_LeavesNoPartialFileWhenItStops()
    {
        using HttpClient client = Serving(new byte[5000]);

        await this.Fetch("https://example.org/big.bin", client, "big.bin", maxBytes: 1000);

        Assert.False(File.Exists(Path.Combine(this._root, "big.bin")));
        Assert.Empty(Directory.GetFiles(this._root, "*.downloading"));
    }

    [Fact]
    public async Task Fetch_RefusesANonSuccessStatus()
    {
        using HttpClient client = Serving(Encoding.UTF8.GetBytes("nope"), status: HttpStatusCode.NotFound);

        Assert.False((await this.Fetch("https://example.org/gone.las", client)).IsOk(out _, out string? error));
        Assert.Contains("404", error);
    }

    [Fact]
    public async Task Fetch_WarnsWhenAPortalReturnsAnErrorPageUnderADataFileName()
    {
        // The failure this exists for: a 200 carrying HTML, saved as tile.las, is indistinguishable
        // from success by every other measure.
        using HttpClient client = Serving(
            Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Not found</body></html>"),
            "text/html");

        Result<DownloadOutcome, string> result = await this.Fetch("https://example.org/tile.las", client);

        Assert.True(result.IsOk(out DownloadOutcome? outcome, out _));
        Assert.NotNull(outcome!.Warning);
        Assert.Contains("HTML page", outcome.Warning);
    }

    [Fact]
    public async Task Fetch_DoesNotWarnWhenHtmlWasWhatWasAskedFor()
    {
        using HttpClient client = Serving(Encoding.UTF8.GetBytes("<html><body>hi</body></html>"), "text/html");

        Result<DownloadOutcome, string> result = await this.Fetch("https://example.org/page.html", client);

        Assert.True(result.IsOk(out DownloadOutcome? outcome, out _));
        Assert.Null(outcome!.Warning);
    }

    [Fact]
    public async Task Fetch_SkipsAFileAlreadyPresentAtTheSameSize()
    {
        byte[] body = Encoding.UTF8.GetBytes("same bytes");
        File.WriteAllBytes(Path.Combine(this._root, "tile.csv"), body);

        using var client = new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            response.Content.Headers.ContentLength = body.Length;
            return response;
        }));

        Result<DownloadOutcome, string> result = await this.Fetch("https://example.org/tile.csv", client);

        Assert.True(result.IsOk(out DownloadOutcome? outcome, out _));
        Assert.True(outcome!.AlreadyPresent);
    }

    [Fact]
    public async Task Fetch_RefusesToReplaceADifferentFileUnlessAsked()
    {
        File.WriteAllText(Path.Combine(this._root, "tile.csv"), "old and shorter");

        using HttpClient client = Serving(Encoding.UTF8.GetBytes("a completely different, longer body"));

        Assert.False((await this.Fetch("https://example.org/tile.csv", client)).IsOk(out _, out string? error));
        Assert.Contains("overwrite", error);

        Assert.True((await this.Fetch("https://example.org/tile.csv", client, overwrite: true)).IsOk(out _, out _));
    }

    [Fact]
    public async Task Fetch_RecordsWhatItFetchedSoAPackageNeedNotCarryIt()
    {
        using HttpClient client = Serving(Encoding.UTF8.GetBytes("point cloud bytes"));

        await this.Fetch("https://example.org/tile.las", client);

        IReadOnlyList<PhyDownloadRecord> ledger = DownloadLedger.Read(this._root);
        PhyDownloadRecord record = Assert.Single(ledger);
        Assert.Equal("https://example.org/tile.las", record.Url);
        Assert.Equal("tile.las", record.File);
        Assert.True(DownloadLedger.IsRefetchable(ledger, "tile.las"));
    }

    [Fact]
    public async Task Fetch_TakesANameFromTheUrlWhenNoneIsGiven()
    {
        using HttpClient client = Serving(Encoding.UTF8.GetBytes("x"));

        Result<DownloadOutcome, string> result =
            await this.Fetch("https://example.org/exports/2026/site%20survey.las", client);

        Assert.True(result.IsOk(out DownloadOutcome? outcome, out _));

        // Sanitized to one safe segment, with the extension kept — it is what tells Rhino how to
        // read the file.
        Assert.Equal("site-survey.las", outcome!.FileName);
    }

    // Serves a refusal with the headers a real one carried, so the challenge detection is tested
    // against the shape Cloudflare actually sends rather than a guess at it.
    private static HttpClient Refusing(
        HttpStatusCode status,
        string? server = null,
        string? contentType = null,
        bool cfMitigated = false)
    {
        return new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent("<!DOCTYPE html><html>challenge</html>"),
            };

            response.Content.Headers.Remove("Content-Type");
            if (contentType is not null)
            {
                response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            if (server is not null)
            {
                response.Headers.TryAddWithoutValidation("Server", server);
            }

            if (cfMitigated)
            {
                response.Headers.TryAddWithoutValidation("Cf-Mitigated", "challenge");
            }

            return response;
        }));
    }

    [Fact]
    public async Task Fetch_NamesABotChallengeAndSaysWhatToDoInstead()
    {
        // Observed live on Vancouver's LiDAR host: 403 with Cf-Mitigated and a challenge page, a full
        // browser User-Agent included. A bare "403 Forbidden" had the model retry four times.
        using HttpClient client = Refusing(
            HttpStatusCode.Forbidden, server: "cloudflare", contentType: "text/html", cfMitigated: true);

        Assert.False((await this.Fetch("https://blocked.example/tile.zip", client)).IsOk(out _, out string? error));

        Assert.True(FileDownload.IsBlocked(error));
        Assert.Contains("not something to retry", error);

        // It has to name the affordance that actually solves this — the browser window that saves
        // into the project folder — and the folder itself, for the hand-carried fallback.
        Assert.Contains("Fetch in Browser", error);
        Assert.Contains(this._root, error);
        Assert.Contains("Do not call download_file", error);
    }

    [Fact]
    public async Task IsBlocked_TellsTheTwoKindsOfFailureApart()
    {
        // The Download File node keys "offer the browser window" off this, so a false positive sends
        // someone to a browser over a typo and a false negative hides the only thing that works.
        using HttpClient blocked = Refusing(
            HttpStatusCode.Forbidden, server: "cloudflare", contentType: "text/html", cfMitigated: true);
        using HttpClient missing = Serving(Encoding.UTF8.GetBytes("nope"), status: HttpStatusCode.NotFound);

        (await this.Fetch("https://blocked.example/a.zip", blocked)).IsOk(out _, out string? blockedError);
        (await this.Fetch("https://example.org/a.zip", missing)).IsOk(out _, out string? missingError);

        Assert.True(FileDownload.IsBlocked(blockedError));
        Assert.False(FileDownload.IsBlocked(missingError));
        Assert.False(FileDownload.IsBlocked(null));
        Assert.False(FileDownload.IsBlocked(string.Empty));
    }

    [Fact]
    public async Task Fetch_DetectsACloudflareChallengeWithoutTheCfHeader()
    {
        // The bare host answered with Server: cloudflare and an HTML body but no Cf-Mitigated, so
        // that combination has to count on its own.
        using HttpClient client = Refusing(
            HttpStatusCode.Forbidden, server: "cloudflare", contentType: "text/html");

        Assert.False((await this.Fetch("https://blocked.example/tile.zip", client)).IsOk(out _, out string? error));
        Assert.Contains("BOT CHALLENGE", error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null, null)]
    [InlineData(HttpStatusCode.Forbidden, "nginx", "text/html")]
    [InlineData(HttpStatusCode.Forbidden, "cloudflare", "application/json")]
    [InlineData(HttpStatusCode.NotFound, "cloudflare", "text/html")]
    [InlineData(HttpStatusCode.Unauthorized, "cloudflare", "text/html")]
    public async Task Fetch_DoesNotCallAnOrdinaryRefusalABotChallenge(
        HttpStatusCode status, string? server, string? contentType)
    {
        // The costly mistake in the other direction: telling the model to give up and fetch a file by
        // hand when the URL was simply wrong, or the file genuinely needs credentials.
        using HttpClient client = Refusing(status, server, contentType);

        Assert.False((await this.Fetch("https://example.org/tile.zip", client)).IsOk(out _, out string? error));
        Assert.DoesNotContain("BOT CHALLENGE", error);
        Assert.Contains(((int)status).ToString(), error);
    }

    [Fact]
    public async Task Fetch_IdentifiesItself()
    {
        // HttpClient sends no User-Agent at all, and plenty of servers refuse a request without one —
        // a failure that looks exactly like a challenge and is not.
        string? seen = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            seen = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
        }));

        await this.Fetch("https://example.org/a.bin", client);

        Assert.NotNull(seen);
        Assert.Contains("Physalia", seen);
    }

    [Fact]
    public void Describe_ReadsAsASizeAPersonWouldWrite()
    {
        Assert.Equal("512 bytes", FileDownload.Describe(512));
        Assert.Equal("1.5 KB", FileDownload.Describe(1500));
        Assert.Equal("2.5 MB", FileDownload.Describe(2_500_000));
        Assert.Equal("1.2 GB", FileDownload.Describe(1_200_000_000));
    }
}
