// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;

namespace Physalia.Core.Providers;

/// <summary>
/// Shared plumbing for all HTTP protocol providers: HTTP client ownership, the once-only
/// config downcast, the identical streaming/model-list call skeletons, request sending with
/// error translation, and resilient stream-line reading. Wire-format concerns — building the
/// request body, parsing the SSE stream, serialising messages — live in each protocol subclass,
/// which receives the already-downcast <typeparamref name="TConfig"/> and never repeats the cast.
/// </summary>
/// <typeparam name="TConfig">The concrete config type this provider's wire format expects.</typeparam>
public abstract class ProtocolProviderBase<TConfig> : ILlmProvider
    where TConfig : ModelConfig
{
    // One HttpClient shared by every provider instance. HttpClient is thread-safe and designed
    // for reuse; a fresh one per provider (or per request) leaks sockets. No per-request state is
    // set on it — auth headers live on each HttpRequestMessage — so a single instance is safe.
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Streams an inference call: downcasts the config, sends the request, and yields the parsed
    /// SSE chunks. The shape is identical for every protocol; subclasses supply only the
    /// provider-specific <see cref="SendHttpRequestAsync"/> and <see cref="ParseSseStreamAsync"/>.
    /// </summary>
    /// <param name="conversation">The conversation history to send.</param>
    /// <param name="systemPrompt">The system prompt, passed at call time and not stored in the conversation.</param>
    /// <param name="config">Provider configuration. Must be a <typeparamref name="TConfig"/> instance.</param>
    /// <param name="tools">Tool definitions to advertise to the model, or null/empty to send none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of result chunks.</returns>
    public async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
        Conversation conversation,
        string systemPrompt,
        ModelConfig config,
        IReadOnlyList<LlmToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(config);

        if (!TryGetConfig(config, out TConfig typed, out LlmError configError))
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(configError);
            yield break;
        }

        var httpResult = await SendHttpRequestAsync(conversation, systemPrompt, typed, tools, ct);
        if (httpResult.IsErr(out var httpErr, out var response))
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(httpErr);
            yield break;
        }

        using HttpResponseMessage owned = response;
        using var stream = await owned.Content.ReadAsStreamAsync(ct);

        await foreach (var chunk in ParseSseStreamAsync(stream, ct))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Returns the model IDs available for the given configuration. Downcasts the config, then
    /// defers to the provider-specific <see cref="FetchAvailableModelsAsync"/>.
    /// </summary>
    /// <param name="config">Provider configuration. Must be a <typeparamref name="TConfig"/> instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of model ID strings, or an error.</returns>
    public async Task<Result<IReadOnlyList<string>, LlmError>> GetAvailableModelsAsync(
        ModelConfig config,
        CancellationToken ct)
    {
        if (!TryGetConfig(config, out TConfig typed, out LlmError configError))
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(configError);
        }

        return await FetchAvailableModelsAsync(typed, ct);
    }

    /// <summary>
    /// Builds and sends the streaming inference request for this protocol. The caller owns and
    /// disposes the returned response.
    /// </summary>
    /// <param name="conversation">The conversation history.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="config">The already-downcast provider configuration.</param>
    /// <param name="tools">Tool definitions to advertise, or null/empty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The open response on success, or an error.</returns>
    protected abstract Task<Result<HttpResponseMessage, LlmError>> SendHttpRequestAsync(
        Conversation conversation,
        string systemPrompt,
        TConfig config,
        IReadOnlyList<LlmToolDefinition>? tools,
        CancellationToken ct);

    /// <summary>
    /// Parses the protocol's streaming response body into response chunks.
    /// </summary>
    /// <param name="stream">The open response body stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of result chunks.</returns>
    protected abstract IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> ParseSseStreamAsync(
        Stream stream,
        CancellationToken ct);

    /// <summary>
    /// Fetches the available model IDs for this protocol from the already-downcast config.
    /// </summary>
    /// <param name="config">The already-downcast provider configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of model ID strings, or an error.</returns>
    protected abstract Task<Result<IReadOnlyList<string>, LlmError>> FetchAvailableModelsAsync(
        TConfig config,
        CancellationToken ct);

    /// <summary>
    /// Checks that <paramref name="config"/> is the concrete config type this provider
    /// expects, producing a standard InvalidRequest error when it is not.
    /// </summary>
    /// <param name="config">The configuration passed by the caller.</param>
    /// <param name="typed">The downcast configuration when the check succeeds.</param>
    /// <param name="error">The error to return when the check fails; otherwise default.</param>
    /// <returns>true if the config is of the expected type; otherwise false.</returns>
    private static bool TryGetConfig(ModelConfig config, out TConfig typed, out LlmError error)
    {
        if (config is TConfig match)
        {
            typed = match;
            error = default!;
            return true;
        }

        typed = default!;
        error = new LlmError(
            LlmErrorKind.InvalidRequest,
            $"Expected {typeof(TConfig).Name} but received {config.GetType().Name}.");
        return false;
    }

    /// <summary>
    /// Reads a single line from a response stream, translating cancellation and IO
    /// failures into errors instead of exceptions so iterator methods can yield them.
    /// </summary>
    /// <param name="reader">The stream reader over the response body.</param>
    /// <returns>The line read (null at end of stream) and an error when reading failed.</returns>
    protected static async Task<(string? Line, LlmError? Error)> ReadStreamLineAsync(StreamReader reader)
    {
        try
        {
            return (await reader.ReadLineAsync(), null);
        }
        catch (OperationCanceledException)
        {
            return (null, new LlmError(LlmErrorKind.Cancelled, "Stream was cancelled."));
        }
        catch (Exception ex)
        {
            return (null, new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    /// <summary>
    /// Sends a streaming request (headers-read completion) and translates HTTP failures,
    /// cancellation, and network errors into <see cref="LlmError"/> results. The caller
    /// owns the returned response and must dispose it.
    /// </summary>
    /// <param name="request">The fully built request, including auth headers and content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The open response on success, or an error.</returns>
    protected async Task<Result<HttpResponseMessage, LlmError>> SendStreamingRequestAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();
                return new Result<HttpResponseMessage, LlmError>.Err(
                    new LlmError(HttpErrorMapper.MapStatusCode(statusCode), errorBody));
            }

            return new Result<HttpResponseMessage, LlmError>.Ok(response);
        }
        catch (OperationCanceledException)
        {
            return new Result<HttpResponseMessage, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Request was cancelled."));
        }
        catch (HttpRequestException ex)
        {
            return new Result<HttpResponseMessage, LlmError>.Err(
                new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    /// <summary>
    /// Sends a request, reads the full response body as a string, and translates HTTP
    /// failures, cancellation, and network errors into <see cref="LlmError"/> results.
    /// </summary>
    /// <param name="request">The fully built request, including auth headers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response body on success, or an error.</returns>
    protected async Task<Result<string, LlmError>> SendForStringAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new Result<string, LlmError>.Err(
                    new LlmError(HttpErrorMapper.MapStatusCode(response.StatusCode), body));
            }

            return new Result<string, LlmError>.Ok(body);
        }
        catch (OperationCanceledException)
        {
            return new Result<string, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Request was cancelled."));
        }
        catch (HttpRequestException ex)
        {
            return new Result<string, LlmError>.Err(
                new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    /// <summary>
    /// Parses a model-list response of the shape <c>{ "data": [ { "id": "..." }, ... ] }</c>
    /// — the format shared by the OpenAI and Anthropic model endpoints.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <returns>The model IDs found, in response order.</returns>
    protected static IReadOnlyList<string> ParseModelIdsFromDataArray(string json)
    {
        using var doc = JsonDocument.Parse(json);

        var ids = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var model in data.EnumerateArray())
            {
                if (model.TryGetProperty("id", out var id))
                {
                    ids.Add(id.GetString() ?? string.Empty);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Parses a tool's JSON Schema string into a node for embedding in a request body. A blank or
    /// unparseable schema falls back to a minimal empty-object schema so the request stays valid.
    /// </summary>
    /// <param name="schemaJson">The tool's input JSON Schema, as a string.</param>
    /// <returns>A JSON node ready to embed as the tool's parameter schema.</returns>
    protected static JsonNode ParseToolSchema(string? schemaJson)
    {
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                JsonNode? parsed = JsonNode.Parse(schemaJson);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Fall through to the minimal object schema below.
            }
        }

        return new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
    }
}
