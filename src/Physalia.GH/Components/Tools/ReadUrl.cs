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
/// A model-invoked tool node that reads a web page: it fetches a URL through the Jina Reader
/// (<c>r.jina.ai</c>) and returns the main content as clean markdown. It advertises a <c>read_url</c>
/// tool (wire its Tool output into the LLM Call's Tools input); when the model calls it, the dispatched
/// signal arrives from a Router, the node fetches the page, and it emits the content as a tool result
/// (wire its Result output through a Feedback component into a Feedback Collector and back to the
/// Router's Results input). The natural follow-up to web_search.
///
/// <para>Keyless by default; an optional Jina key (section <c>web_search</c>, leaf <c>jina</c>, or the
/// <c>JINA_API_KEY</c> environment variable) raises rate limits. The HTTP call is bounded by a timeout
/// and run synchronously within the dispatched call.</para>
/// </summary>
public class ReadUrl : ToolComponentBase
{
    // Shared, not per-instance: HttpClient is thread-safe and reuse avoids socket exhaustion.
    private static readonly HttpClient _httpClient = new();

    private const int TimeoutMs = 30000;

    private static readonly ToolDefinition ToolDef = new(
        "read_url",
        "Fetch a web page and read its main content as clean text. Call this to read a specific URL in full — typically a result returned by web_search. Returns the page content (truncated to max_chars).",
        "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"The absolute http(s) URL to fetch and read.\"},\"max_chars\":{\"type\":\"integer\",\"description\":\"Truncate the returned text to this many characters.\",\"default\":8000}},\"required\":[\"url\"]}");

    private string? _jinaKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadUrl"/> class.
    /// </summary>
    public ReadUrl()
        : base("Read URL", "ReadURL", "A tool the model calls to fetch and read a web page (Jina Reader).")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7F8E6EB2-B012-4068-A90B-D9EF87229B7F");

    /// <inheritdoc/>
    protected override ToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    /// <remarks>Resolve the optional Jina key once per solve; the tool works without it.</remarks>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        _jinaKey = WebToolKeys.Resolve("jina");
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        (string url, int maxChars) = ParseArgs(call.InputJson);
        if (string.IsNullOrWhiteSpace(url))
        {
            return ToolCallResult.Error("read_url requires a non-empty 'url'.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeoutMs);

        Result<string, LlmError> result = await WebTools
            .FetchUrlAsync(url, maxChars, _jinaKey, _httpClient, timeout.Token)
            .ConfigureAwait(false);

        return result.IsOk(out string? text, out LlmError? error)
            ? ToolCallResult.Ok(text)
            : ToolCallResult.Error($"Could not fetch {url} ({error.Kind}): {error.Message}");
    }

    private static (string Url, int MaxChars) ParseArgs(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return (string.Empty, 8000);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (inputJson.Trim(), 8000);
            }

            string url = root.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() ?? string.Empty
                : string.Empty;

            int maxChars = 8000;
            if (root.TryGetProperty("max_chars", out JsonElement m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out int parsed))
            {
                maxChars = parsed;
            }

            return (url, maxChars);
        }
        catch (JsonException)
        {
            return (inputJson.Trim(), 8000);
        }
    }
}
