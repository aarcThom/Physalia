// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;

namespace Physalia.Core.Providers;

/// <summary>
/// Shared plumbing for all protocol providers: HTTP client ownership, config type
/// guarding, request sending with error translation, and resilient stream-line reading.
/// Wire-format concerns (request bodies, SSE event parsing, message serialisation)
/// remain in each protocol subclass.
/// </summary>
public abstract class ProtocolProviderBase : ILlmProvider
{
    /// <summary>
    /// Shared HTTP client. Instantiated once per provider instance and never per-request.
    /// </summary>
    protected readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolProviderBase"/> class.
    /// </summary>
    protected ProtocolProviderBase()
    {
        _httpClient = new HttpClient();
    }

    /// <inheritdoc/>
    public abstract IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
        Conversation conversation,
        string systemPrompt,
        ModelConfig config,
        CancellationToken ct);

    /// <inheritdoc/>
    public abstract Task<Result<IReadOnlyList<string>, LlmError>> GetAvailableModelsAsync(
        ModelConfig config,
        CancellationToken ct);

    /// <summary>
    /// Checks that <paramref name="config"/> is the concrete config type this provider
    /// expects, producing a standard InvalidRequest error when it is not.
    /// </summary>
    /// <typeparam name="TConfig">The expected concrete config type.</typeparam>
    /// <param name="config">The configuration passed by the caller.</param>
    /// <param name="typed">The downcast configuration when the check succeeds.</param>
    /// <param name="error">The error to return when the check fails; otherwise default.</param>
    /// <returns>true if the config is of the expected type; otherwise false.</returns>
    protected static bool TryGetConfig<TConfig>(ModelConfig config, out TConfig typed, out LlmError error)
        where TConfig : ModelConfig
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
}
