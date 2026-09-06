// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net.Http;
using Physalia.Core.Common;

namespace Physalia.Core.Api;

/// <summary>
/// Composes and sends one read-only request against a configured <see cref="ApiEndpoint"/>.
/// </summary>
/// <remarks>
/// <para><b>The model supplies a path and a query string, never a URL.</b> That is the whole
/// security posture of this tool, and <see cref="ComposeUri"/> enforces it rather than trusting the
/// result: relative resolution alone is not a containment guarantee, because
/// <c>new Uri(baseUri, "https://elsewhere/")</c> quietly returns the other host. So the composed URI
/// is checked back against the base — same scheme, same authority, and a path still beneath the
/// base's — and anything else is refused before a socket is opened.</para>
/// <para><b>GET only.</b> A model-authored request body is a much larger surface than a query
/// string, and reading is what this tier is for. A write API belongs behind a node the human wires
/// deliberately, not behind a tool the model may call on its own initiative.</para>
/// </remarks>
public static class ApiRequest
{
    /// <summary>
    /// Builds the absolute URI for one call, refusing anything that leaves the endpoint's base.
    /// </summary>
    /// <param name="endpoint">The configured endpoint.</param>
    /// <param name="path">The path relative to the base URL, as the model supplied it.</param>
    /// <param name="query">The raw query string, with or without a leading "?"; may be empty.</param>
    /// <param name="key">The resolved key, used only when the endpoint puts it in the query.</param>
    /// <returns>The composed URI, or the reason it was refused.</returns>
    public static Result<Uri, string> ComposeUri(ApiEndpoint endpoint, string? path, string? query, string? key)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return new Result<Uri, string>.Err(
                $"'{endpoint.Name}' has no valid http(s) base URL. Set one in the chat window.");
        }

        string relative = (path ?? string.Empty).Trim();

        // An absolute path would replace the host outright, which is exactly what must not happen.
        if (Uri.TryCreate(relative, UriKind.Absolute, out _))
        {
            return new Result<Uri, string>.Err(
                "Pass a path relative to the configured base URL, not a whole URL.");
        }

        // Second line of defence for a protocol-relative "//other-host/path". On Windows the check
        // above already refuses it — .NET parses that as an absolute file:// UNC URI — but off
        // Windows it is not absolute and reaches here, where losing its slashes makes it an ordinary
        // path segment beneath the base. Two platforms, two mechanisms, the same containment.
        relative = relative.TrimStart('/');

        // The base is a DIRECTORY. Without the trailing slash, relative resolution drops its last
        // segment, so a base of ".../v2.1" would silently resolve "catalog" against ".../".
        string baseText = baseUri.AbsoluteUri;
        if (!baseText.EndsWith("/", StringComparison.Ordinal))
            baseUri = new Uri(baseText + "/");

        if (!Uri.TryCreate(baseUri, relative, out Uri? composed))
            return new Result<Uri, string>.Err($"Could not build a URL from path '{path}'.");

        if (!string.Equals(composed.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(composed.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return new Result<Uri, string>.Err(
                $"That path leaves {baseUri.Authority}, which this endpoint does not allow.");
        }

        // Relative resolution honours "..", so a path may still climb above the configured root
        // while staying on the same host. An endpoint scoped to one API version means that version.
        if (!composed.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.OrdinalIgnoreCase))
        {
            return new Result<Uri, string>.Err(
                $"That path climbs above the configured base {baseUri.AbsolutePath}.");
        }

        string merged = MergeQuery(composed.Query, query, endpoint, key);
        var builder = new UriBuilder(composed) { Query = merged };
        return new Result<Uri, string>.Ok(builder.Uri);
    }

    /// <summary>
    /// Sends one GET request and returns its body.
    /// </summary>
    /// <param name="endpoint">The configured endpoint.</param>
    /// <param name="path">The path relative to the base URL.</param>
    /// <param name="query">The raw query string; may be empty.</param>
    /// <param name="key">The resolved key, or null when the endpoint needs none.</param>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token; bound the call with a timeout.</param>
    /// <returns>The response body, or an error.</returns>
    public static async Task<Result<string, LlmError>> SendAsync(
        ApiEndpoint endpoint,
        string? path,
        string? query,
        string? key,
        HttpClient client,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(client);

        if (ComposeUri(endpoint, path, query, key).IsErr(out string? refusal, out Uri? uri))
            return new Result<string, LlmError>.Err(new LlmError(LlmErrorKind.InvalidRequest, refusal));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuthHeader(request, endpoint, key);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain;q=0.9, */*;q=0.8");

            using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // The body carries the API's own complaint — a bad field name, a malformed filter —
                // which is the one thing that lets the model correct itself on the next call. Passing
                // only the status code back would make every mistake look the same.
                string detail = body.Length > 500 ? body.Substring(0, 500) : body;
                return new Result<string, LlmError>.Err(new LlmError(
                    HttpErrorMapper.MapStatusCode(response.StatusCode),
                    $"{(int)response.StatusCode} from {endpoint.Name}: {detail}"));
            }

            return new Result<string, LlmError>.Ok(body);
        }
        catch (OperationCanceledException)
        {
            return new Result<string, LlmError>.Err(
                new LlmError(LlmErrorKind.Timeout, $"The request to {endpoint.Name} timed out."));
        }
        catch (HttpRequestException ex)
        {
            return new Result<string, LlmError>.Err(new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    /// <summary>
    /// Sends as many GET requests as it takes to gather up to <paramref name="maxRecords"/> records,
    /// and returns one body per page in fetch order.
    /// </summary>
    /// <remarks>
    /// <para><b>The page size is never assumed, it is measured.</b> Whatever the first response
    /// actually contained is the stride for the next offset, so an API capping pages at 100, 50 or 20
    /// all walk correctly with nothing configured. Assuming a size means either refetching rows or
    /// skipping them, and skipping is silent.</para>
    /// <para><b>A failure part way through keeps what was already gathered.</b> Page four 400ing —
    /// which is what Opendatasoft does past offset+limit 10000 — must not throw away the three pages
    /// that worked; the caller is told paging stopped early and why. Only a failure on the FIRST page
    /// is an error, because then there is nothing to report but the failure.</para>
    /// </remarks>
    /// <param name="endpoint">The configured endpoint.</param>
    /// <param name="path">The path relative to the base URL.</param>
    /// <param name="query">The raw query string as the model wrote it; may be empty.</param>
    /// <param name="key">The resolved key, or null when the endpoint needs none.</param>
    /// <param name="maxRecords">Stop once this many records have been gathered.</param>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token; bound the whole walk with a timeout.</param>
    /// <returns>The pages gathered, or an error when the very first request failed.</returns>
    public static async Task<Result<ApiPagedResponse, LlmError>> SendPagedAsync(
        ApiEndpoint endpoint,
        string? path,
        string? query,
        string? key,
        int maxRecords,
        HttpClient client,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var pages = new List<string>();
        int gathered = 0;
        int offset = 0;
        int? matched = null;
        string? stoppedBecause = null;

        // A ceiling on REQUESTS as well as records. Nothing below can loop forever — a page returning
        // nothing ends the walk — but a misconfigured endpoint paired with a huge Max Records could
        // still fire hundreds of requests before satisfying it, and that is the user's quota.
        //
        // It is the RUNAWAY guard, not the real bound; maxRecords is. So it has to sit well clear of
        // ordinary use, or it silently becomes the limit for any API with a small page: at 50 it took
        // over from a 1000-record request the moment a page held 20 rows, and the walk then reported
        // stopping for a reason that had nothing to do with what was asked for.
        const int MaxPages = 100;

        for (int page = 0; page < MaxPages; page++)
        {
            string pageQuery = endpoint.Paging == ApiPaging.LimitOffset && offset > 0
                ? WithOffset(query, offset)
                : query ?? string.Empty;

            Result<string, LlmError> result =
                await SendAsync(endpoint, path, pageQuery, key, client, ct).ConfigureAwait(false);

            if (result.IsErr(out LlmError? error, out string? body))
            {
                if (pages.Count == 0)
                    return new Result<ApiPagedResponse, LlmError>.Err(error);

                stoppedBecause = error.Message;
                break;
            }

            pages.Add(body);

            (int count, int? total) = ApiResponseSummary.CountRecords(body);
            matched ??= total;
            gathered += count;

            if (endpoint.Paging != ApiPaging.LimitOffset)
            {
                break;
            }

            if (count == 0)
            {
                break;
            }

            if (gathered >= maxRecords)
            {
                stoppedBecause = matched is { } all && all > gathered
                    ? $"stopped at the {maxRecords}-record limit set on the node; {all} matched in total"
                    : null;
                break;
            }

            if (matched is { } totalCount && gathered >= totalCount)
            {
                break;
            }

            offset += count;

            if (page == MaxPages - 1)
            {
                stoppedBecause = $"stopped after {MaxPages} requests";
            }
        }

        return new Result<ApiPagedResponse, LlmError>.Ok(
            new ApiPagedResponse(pages, gathered, matched, stoppedBecause));
    }

    /// <summary>
    /// Replaces (or adds) the <c>offset</c> parameter in a query string.
    /// </summary>
    /// <remarks>
    /// Replacing rather than appending matters: a model that wrote its own <c>offset=0</c> would
    /// otherwise get two, and which one an API honours is its own business.
    /// </remarks>
    /// <param name="query">The query string, with or without a leading "?".</param>
    /// <param name="offset">The offset to set.</param>
    /// <returns>The query string carrying exactly one offset parameter.</returns>
    public static string WithOffset(string? query, int offset)
    {
        IEnumerable<string> kept = (query ?? string.Empty)
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("offset=", StringComparison.OrdinalIgnoreCase));

        return string.Join("&", kept.Append("offset=" + offset));
    }

    /// <summary>
    /// Describes a URI with any key removed, safe to show on a node or write to a trace.
    /// </summary>
    /// <param name="endpoint">The endpoint the URI was built for.</param>
    /// <param name="uri">The composed URI.</param>
    /// <returns>The URI text with a query-string key masked.</returns>
    public static string Redact(ApiEndpoint endpoint, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(uri);

        if (endpoint.Auth != ApiAuth.QueryParameter || string.IsNullOrWhiteSpace(endpoint.AuthName))
            return uri.AbsoluteUri;

        string[] parts = uri.Query.TrimStart('?').Split('&');
        for (int i = 0; i < parts.Length; i++)
        {
            int eq = parts[i].IndexOf('=');
            string name = eq < 0 ? parts[i] : parts[i].Substring(0, eq);
            if (string.Equals(Uri.UnescapeDataString(name), endpoint.AuthName, StringComparison.OrdinalIgnoreCase))
                parts[i] = name + "=***";
        }

        return new UriBuilder(uri) { Query = string.Join("&", parts) }.Uri.AbsoluteUri;
    }

    private static void ApplyAuthHeader(HttpRequestMessage request, ApiEndpoint endpoint, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        switch (endpoint.Auth)
        {
            case ApiAuth.BearerHeader:
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                break;

            case ApiAuth.CustomHeader when !string.IsNullOrWhiteSpace(endpoint.AuthName):
                request.Headers.TryAddWithoutValidation(endpoint.AuthName, endpoint.AuthPrefix + key);
                break;
        }
    }

    private static string MergeQuery(string composedQuery, string? modelQuery, ApiEndpoint endpoint, string? key)
    {
        var parts = new List<string>();

        void Add(string? raw)
        {
            string trimmed = (raw ?? string.Empty).TrimStart('?').Trim();
            if (trimmed.Length > 0)
                parts.Add(trimmed);
        }

        Add(composedQuery);
        Add(modelQuery);

        // The key goes on LAST so a model-supplied parameter of the same name cannot shadow it.
        if (endpoint.Auth == ApiAuth.QueryParameter
            && !string.IsNullOrWhiteSpace(endpoint.AuthName)
            && !string.IsNullOrWhiteSpace(key))
        {
            parts.Add(Uri.EscapeDataString(endpoint.AuthName) + "=" + Uri.EscapeDataString(key));
        }

        return string.Join("&", parts);
    }
}
