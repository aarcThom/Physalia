// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net.Http;
using System.Text;
using System.Text.Json;
using Physalia.Core.Common;

namespace Physalia.Core.Web;

/// <summary>
/// Pure HTTP helpers behind the model-invoked web tools: an internet search (Tavily) and a
/// read-URL fetch (Jina Reader). Each returns a compact, LLM-ready text body or an
/// <see cref="LlmError"/>. Callers pass a shared <see cref="HttpClient"/> and a cancellation token
/// (use it to bound the call); every await uses <c>ConfigureAwait(false)</c> so a synchronous tool
/// can block on the result without deadlocking the Grasshopper solve thread.
/// </summary>
public static class WebTools
{
    private const string TavilySearchUrl = "https://api.tavily.com/search";
    private const string JinaReaderBase = "https://r.jina.ai/";

    /// <summary>
    /// Searches the web via the Tavily API and returns a compact result block (a synthesized answer
    /// when available, then numbered title / URL / snippet lines).
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Desired result count; clamped to 1–10.</param>
    /// <param name="apiKey">The Tavily API key.</param>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token (bound the call with a timeout).</param>
    /// <returns>The formatted result text, or an error.</returns>
    public static async Task<Result<string, LlmError>> SearchTavilyAsync(
        string query,
        int maxResults,
        string apiKey,
        HttpClient client,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Err(LlmErrorKind.InvalidRequest, "Search query was empty.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Err(LlmErrorKind.Auth, "No Tavily API key was provided.");
        }

        int count = Math.Clamp(maxResults <= 0 ? 5 : maxResults, 1, 10);
        string requestJson = JsonSerializer.Serialize(new
        {
            query,
            max_results = count,
            include_answer = "basic",
        });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TavilySearchUrl);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Err(HttpErrorMapper.MapStatusCode(response.StatusCode), Truncate(body, 300));
            }

            return new Result<string, LlmError>.Ok(FormatTavily(body, query));
        }
        catch (OperationCanceledException)
        {
            return Err(LlmErrorKind.Timeout, "Search request timed out or was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return Err(LlmErrorKind.Network, ex.Message);
        }
    }

    /// <summary>
    /// Fetches a URL through the Jina Reader (<c>r.jina.ai</c>) and returns its main content as clean
    /// markdown, truncated to <paramref name="maxChars"/>. Keyless; an optional Jina key raises limits.
    /// </summary>
    /// <param name="url">The absolute http(s) URL to read.</param>
    /// <param name="maxChars">Maximum characters to return; ≤ 0 defaults to 8000.</param>
    /// <param name="jinaApiKey">Optional Jina API key (null/empty for the keyless path).</param>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token (bound the call with a timeout).</param>
    /// <returns>The page markdown (truncated), or an error.</returns>
    public static async Task<Result<string, LlmError>> FetchUrlAsync(
        string url,
        int maxChars,
        string? jinaApiKey,
        HttpClient client,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Err(LlmErrorKind.InvalidRequest, "No URL was provided.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Err(LlmErrorKind.InvalidRequest, $"Not a valid http(s) URL: {url}");
        }

        int cap = maxChars <= 0 ? 8000 : maxChars;

        try
        {
            // Jina Reader takes the target URL appended to its base, verbatim.
            using var request = new HttpRequestMessage(HttpMethod.Get, JinaReaderBase + url);
            request.Headers.TryAddWithoutValidation("X-Respond-With", "markdown");
            if (!string.IsNullOrWhiteSpace(jinaApiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + jinaApiKey);
            }

            using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Err(HttpErrorMapper.MapStatusCode(response.StatusCode), Truncate(body, 300));
            }

            return new Result<string, LlmError>.Ok(Truncate(body.Trim(), cap));
        }
        catch (OperationCanceledException)
        {
            return Err(LlmErrorKind.Timeout, "Fetch request timed out or was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return Err(LlmErrorKind.Network, ex.Message);
        }
    }

    private static string FormatTavily(string json, string query)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            var sb = new StringBuilder();

            if (root.TryGetProperty("answer", out JsonElement answer)
                && answer.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(answer.GetString()))
            {
                sb.AppendLine("Answer: " + answer.GetString()!.Trim());
                sb.AppendLine();
            }

            int n = 0;
            if (root.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement r in results.EnumerateArray())
                {
                    n++;
                    string title = GetString(r, "title");
                    string url = GetString(r, "url");
                    string content = GetString(r, "content");

                    sb.AppendLine($"{n}. {title} — {url}");
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        sb.AppendLine("   " + Truncate(content.Replace('\n', ' ').Replace('\r', ' ').Trim(), 300));
                    }
                }
            }

            string text = sb.ToString().TrimEnd();
            return text.Length > 0 ? text : $"No results for \"{query}\".";
        }
        catch (JsonException)
        {
            return Truncate(json, 2000);
        }
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value.Substring(0, max) + $"\n… [truncated {value.Length - max} characters]";
    }

    private static Result<string, LlmError> Err(LlmErrorKind kind, string message) =>
        new Result<string, LlmError>.Err(new LlmError(kind, message));
}
