// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Web;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that searches the internet via the Tavily API. It advertises a
/// <c>web_search</c> tool (wire its Tool output into the LLM Call's Tools input); when the model calls
/// it, the dispatched signal arrives from a Router, the node queries Tavily, and it emits the result
/// as a tool result (wire its Result output through a Feedback component into a Feedback Collector and
/// back to the Router's Results input).
///
/// <para>The Tavily key resolves from <c>Files/API_KEY_CONFIG.YAML</c> (section <c>web_search</c>, leaf
/// <c>tavily</c>) or the <c>TAVILY_API_KEY</c> environment variable — never serialized. The HTTP call
/// is bounded by a timeout and run synchronously within the dispatched call.</para>
/// </summary>
public class WebSearch : LlmToolComponentBase
{
    // Shared, not per-instance: HttpClient is thread-safe and reuse avoids socket exhaustion.
    private static readonly HttpClient _httpClient = new();

    private const int TimeoutMs = 20000;

    private static readonly LlmToolDefinition ToolDef = new(
        "web_search",
        "Search the internet for current information, documentation, or facts beyond your training data. Returns a synthesized answer (when available) plus a short list of result titles, URLs, and snippets. Follow up with read_url to read a specific result in full.",
        "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"The search query.\"},\"count\":{\"type\":\"integer\",\"description\":\"Number of results to return (1-10).\",\"default\":5}},\"required\":[\"query\"]}");

    private string? _apiKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSearch"/> class.
    /// </summary>
    public WebSearch()
        : base("Web Search", "WebSearch", "A tool the model calls to search the internet (Tavily).")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("02315974-8633-4BCF-B4B3-9C33DC193778");

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    /// <remarks>Resolve the key once per solve so each dispatched call reuses it.</remarks>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        _apiKey = WebToolKeys.Resolve("tavily");
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        if (_apiKey is null)
        {
            return ToolCallResult.Error(
                "No Tavily API key configured. Add web_search.tavily to API_KEY_CONFIG.YAML or set the TAVILY_API_KEY environment variable.");
        }

        (string query, int count) = ParseArgs(call.InputJson);
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolCallResult.Error("web_search requires a non-empty 'query'.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeoutMs);

        Result<string, LlmError> result = await WebTools
            .SearchTavilyAsync(query, count, _apiKey, _httpClient, timeout.Token)
            .ConfigureAwait(false);

        return result.IsOk(out string? text, out LlmError? error)
            ? ToolCallResult.Ok(text)
            : ToolCallResult.Error($"Search failed ({error.Kind}): {error.Message}");
    }

    private static (string Query, int Count) ParseArgs(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return (string.Empty, 5);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (inputJson, 5);
            }

            string query = root.TryGetProperty("query", out JsonElement q) && q.ValueKind == JsonValueKind.String
                ? q.GetString() ?? string.Empty
                : string.Empty;

            int count = 5;
            if (root.TryGetProperty("count", out JsonElement c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int parsed))
            {
                count = parsed;
            }

            return (query, count);
        }
        catch (JsonException)
        {
            // Treat the raw argument string as the query.
            return (inputJson, 5);
        }
    }
}
