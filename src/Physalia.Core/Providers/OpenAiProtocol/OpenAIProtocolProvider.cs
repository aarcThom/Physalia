// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.Conversations;
using Physalia.Core.Models;
using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Providers.OpenAiProtocol;

/// <summary>
/// Abstract base class for all providers that speak the OpenAI Chat Completions wire format.
/// Implements HTTP transport, SSE parsing, and message serialisation once.
/// Subclasses override <see cref="BuildRequestBody"/> to inject provider-specific parameters.
/// </summary>
public abstract class OpenAIProtocolProvider
{
    /// <summary>
    /// Shared HTTP client. Instantiated once per provider instance and never per-request.
    /// </summary>
    protected readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIProtocolProvider"/> class.
    /// </summary>
    protected OpenAIProtocolProvider()
    {
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Streams an inference call to the provider and yields response chunks as they arrive.
    /// </summary>
    /// <param name="conversation">The conversation history to send.</param>
    /// <param name="systemPrompt">The system prompt, passed at call time and not stored in the conversation.</param>
    /// <param name="config">Provider configuration. Must be an <see cref="OpenAIProtocolConfig"/> instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of result chunks.</returns>
    public async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
        Conversation conversation,
        string systemPrompt,
        ModelConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(config);

        if (config is not OpenAIProtocolConfig openAIConfig)
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(
                new LlmError(LlmErrorKind.InvalidRequest,
                    $"Expected {nameof(OpenAIProtocolConfig)} but received {config.GetType().Name}."));
            yield break;
        }

        var httpResult = await SendHttpRequestAsync(conversation, systemPrompt, openAIConfig, ct);

        if (httpResult is Result<HttpResponseMessage, LlmError>.Err httpErr)
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(httpErr.Error);
            yield break;
        }

        using var response = ((Result<HttpResponseMessage, LlmError>.Ok)httpResult).Value;
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        await foreach (var chunk in ParseSseStreamAsync(stream, ct))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Builds the JSON request body. Override in subclasses to add or strip provider-specific fields
    /// (e.g. DeepSeek thinking mode, Ollama keep_alive, OpenRouter provider routing).
    /// </summary>
    /// <param name="conversation">The conversation history.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="config">The provider configuration.</param>
    /// <returns>A <see cref="JsonObject"/> ready for serialisation.</returns>
    protected virtual JsonObject BuildRequestBody(
        Conversation conversation,
        string systemPrompt,
        OpenAIProtocolConfig config)
    {
        return new JsonObject
        {
            ["model"] = config.ModelId,
            ["stream"] = true,
            ["max_tokens"] = config.MaxTokens,
            ["temperature"] = config.Temperature,
            ["top_p"] = config.TopP,
            ["messages"] = BuildMessagesArray(conversation, systemPrompt),
        };
    }

    private async Task<Result<HttpResponseMessage, LlmError>> SendHttpRequestAsync(
        Conversation conversation,
        string systemPrompt,
        OpenAIProtocolConfig config,
        CancellationToken ct)
    {
        var body = BuildRequestBody(conversation, systemPrompt, config);
        var json = body.ToJsonString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();
                return new Result<HttpResponseMessage, LlmError>.Err(
                    new LlmError(MapStatusCode(statusCode), errorBody));
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

    private static async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> ParseSseStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            string? line = null;
            bool wasCancelled = false;
            Exception? readError = null;

            try
            {
                line = await reader.ReadLineAsync();
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
            }
            catch (Exception ex)
            {
                readError = ex;
            }

            if (wasCancelled)
            {
                yield return new Result<LlmResponseChunk, LlmError>.Err(
                    new LlmError(LlmErrorKind.Cancelled, "Stream was cancelled."));
                yield break;
            }

            if (readError != null)
            {
                yield return new Result<LlmResponseChunk, LlmError>.Err(
                    new LlmError(LlmErrorKind.Network, readError.Message));
                yield break;
            }

            if (line == null) break;
            if (line.Length == 0) continue;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line.Substring(6);
            if (data == "[DONE]") break;

            Result<LlmResponseChunk, LlmError>? parsed = null;
            Exception? parseError = null;

            try
            {
                parsed = ParseSseChunk(data);
            }
            catch (Exception ex)
            {
                parseError = ex;
            }

            if (parseError != null)
            {
                yield return new Result<LlmResponseChunk, LlmError>.Err(
                    new LlmError(LlmErrorKind.InvalidRequest, $"Failed to parse chunk: {parseError.Message}"));
                yield break;
            }

            if (parsed is not null)
            {
                yield return parsed;
            }
        }

        if (ct.IsCancellationRequested)
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Stream was cancelled."));
        }
    }

    private static Result<LlmResponseChunk, LlmError> ParseSseChunk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? contentDelta = null;
        bool isLast = false;

        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];

            if (choice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                contentDelta = content.GetString();
            }

            if (choice.TryGetProperty("finish_reason", out var finishReason) &&
                finishReason.ValueKind != JsonValueKind.Null)
            {
                isLast = true;
            }
        }

        return new Result<LlmResponseChunk, LlmError>.Ok(
            new LlmResponseChunk(contentDelta, isLast, null));
    }

    private static JsonArray BuildMessagesArray(Conversation conversation, string systemPrompt)
    {
        var messages = new JsonArray();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = systemPrompt });
        }

        foreach (var message in conversation.Messages)
        {
            messages.Add(BuildMessage(message));
        }

        return messages;
    }

    private static JsonNode BuildMessage(ConversationMessage message)
    {
        var role = message.Role == Role.User ? "user" : "assistant";

        // Single text block: use the compact string form rather than a content array.
        if (message.Content.Count == 1 && message.Content[0] is TextContent text)
        {
            return new JsonObject { ["role"] = role, ["content"] = text.Text };
        }

        var contentArray = new JsonArray();
        foreach (var block in message.Content)
        {
            contentArray.Add(BuildContentBlock(block));
        }

        return new JsonObject { ["role"] = role, ["content"] = contentArray };
    }

    private static JsonNode BuildContentBlock(MessageContent block)
    {
        return block switch
        {
            TextContent text => new JsonObject
            {
                ["type"] = "text",
                ["text"] = text.Text,
            },

            ImageContent { Source: InlineImage img } => new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = $"data:{img.MimeType};base64,{Convert.ToBase64String(img.Data)}",
                },
            },

            ImageContent { Source: UrlImage url } => new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = url.Url },
            },

            ImageContent { Source: ManagedImage } =>
                throw new InvalidOperationException(
                    "OpenAI Chat Completions does not support managed file references. Use InlineImage or UrlImage."),

            _ => throw new InvalidOperationException(
                    $"Unsupported content block type: {block.GetType().Name}."),
        };
    }

    private static LlmErrorKind MapStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => LlmErrorKind.Auth,
        HttpStatusCode.Forbidden => LlmErrorKind.Auth,
        HttpStatusCode.TooManyRequests => LlmErrorKind.RateLimit,
        HttpStatusCode.BadRequest => LlmErrorKind.InvalidRequest,
        HttpStatusCode.UnprocessableEntity => LlmErrorKind.InvalidRequest,
        _ => LlmErrorKind.Network,
    };
}
