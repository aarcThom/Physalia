// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Tokens;

/// <summary>
/// Async token counting via provider-specific API endpoints.
/// Each method makes a single lightweight HTTP call and returns an exact count.
/// Pass in the caller's shared <see cref="HttpClient"/> — do not instantiate one per call.
/// </summary>
public static class AsyncTokenEstimation
{
    private const string AnthropicVersion = "2023-06-01";

    /// <summary>
    /// Counts input tokens for the given instructions using the Anthropic
    /// <c>POST /v1/messages/count_tokens</c> endpoint.
    /// </summary>
    /// <param name="instructions">The instructions to measure.</param>
    /// <param name="config">Anthropic provider configuration.</param>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exact input token count, or an error.</returns>
    public static async Task<Result<int, LlmError>> CountAnthropicAsync(
        Instructions instructions,
        AnthropicProtocolConfig config,
        HttpClient client,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = config.ModelId,
            ["messages"] = BuildAnthropicMessages(instructions.Conversation),
        };

        if (!string.IsNullOrEmpty(instructions.SystemPrompt))
        {
            body["system"] = instructions.SystemPrompt;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{config.BaseUrl}/messages/count_tokens");

        request.Headers.Add("x-api-key", config.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return new Result<int, LlmError>.Err(
                    new LlmError(MapStatusCode(response.StatusCode), errorBody));
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("input_tokens", out var tokens))
            {
                return new Result<int, LlmError>.Ok(tokens.GetInt32());
            }

            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.InvalidRequest, "Response did not contain 'input_tokens'."));
        }
        catch (OperationCanceledException)
        {
            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Request was cancelled."));
        }
        catch (HttpRequestException ex)
        {
            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    /// <summary>
    /// Counts tokens for the given instructions using the llama.cpp native
    /// <c>POST /tokenize</c> endpoint. Serialises the full conversation to a single
    /// string before sending — exact for the loaded model, regardless of vocabulary.
    /// Accepts any OpenAI-compatible config; only <see cref="OpenAIProtocolConfig.BaseUrl"/>
    /// and <see cref="OpenAIProtocolConfig.ApiKey"/> are used.
    /// </summary>
    /// <param name="instructions">The instructions to measure.</param>
    /// <param name="config">Any OpenAI-compatible provider configuration.</param>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exact token count for the loaded model, or an error.</returns>
    public static async Task<Result<int, LlmError>> CountLlamaCppAsync(
        Instructions instructions,
        OpenAIProtocolConfig config,
        HttpClient client,
        CancellationToken ct = default)
    {
        // The native /tokenize endpoint lives at the server root, not under /v1.
        string baseUrl = config.BaseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? config.BaseUrl[..^3]
            : config.BaseUrl;

        var body = new JsonObject
        {
            ["content"] = SerializeToText(instructions),
            ["add_special"] = true,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/tokenize");

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return new Result<int, LlmError>.Err(
                    new LlmError(LlmErrorKind.Network, errorBody));
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("tokens", out var tokens))
            {
                return new Result<int, LlmError>.Ok(tokens.GetArrayLength());
            }

            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.InvalidRequest, "Response did not contain 'tokens' array."));
        }
        catch (OperationCanceledException)
        {
            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Request was cancelled."));
        }
        catch (HttpRequestException ex)
        {
            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    /// <summary>
    /// Counts input tokens via the Gemini <c>:countTokens</c> API endpoint.
    /// Makes an API call to Google. Exact for the specified Gemini model.
    /// </summary>
    /// <param name="instructions">The instructions to measure.</param>
    /// <param name="config">Gemini provider configuration.</param>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exact input token count, or an error.</returns>
    public static async Task<Result<int, LlmError>> CountGeminiAsync(
        Instructions instructions,
        GeminiProtocolConfig config,
        HttpClient client,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["contents"] = BuildGeminiContents(instructions.Conversation),
        };

        if (!string.IsNullOrEmpty(instructions.SystemPrompt))
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = instructions.SystemPrompt },
                },
            };
        }

        string url = $"{config.BaseUrl}/models/{config.ModelId}:countTokens?key={config.ApiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return new Result<int, LlmError>.Err(
                    new LlmError(MapStatusCode(response.StatusCode), errorBody));
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("totalTokens", out var tokens))
            {
                return new Result<int, LlmError>.Ok(tokens.GetInt32());
            }

            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.InvalidRequest, "Response did not contain 'totalTokens'."));
        }
        catch (OperationCanceledException)
        {
            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Request was cancelled."));
        }
        catch (HttpRequestException ex)
        {
            return new Result<int, LlmError>.Err(
                new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    // HELPERS =====================================================================================

    /// <summary>
    /// Serialises an Instructions object to a plain text string for providers
    /// that tokenise a single string rather than a structured messages array.
    /// </summary>
    private static string SerializeToText(Instructions instructions)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(instructions.SystemPrompt))
        {
            sb.AppendLine($"System: {instructions.SystemPrompt}");
        }

        foreach (var message in instructions.Conversation.Messages)
        {
            string role = message.Role == Role.User ? "User" : "Assistant";
            sb.Append($"{role}: ");

            foreach (var block in message.Content)
            {
                sb.Append(block switch
                {
                    TextContent text => text.Text,
                    ToolCallContent call => $"[tool_call: {call.Name}({call.InputJson})]",
                    ToolResultContent result => $"[tool_result: {result.Content}]",
                    ImageContent => "[image]",
                    _ => string.Empty,
                });
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static JsonArray BuildGeminiContents(Conversation conversation)
    {
        var contents = new JsonArray();

        foreach (var message in conversation.Messages)
        {
            // Gemini uses "model" for the assistant role.
            string role = message.Role == Role.User ? "user" : "model";

            var parts = new JsonArray();
            foreach (var block in message.Content)
            {
                string text = block switch
                {
                    TextContent t => t.Text,
                    ToolCallContent call => $"[tool_call: {call.Name}({call.InputJson})]",
                    ToolResultContent result => $"[tool_result: {result.Content}]",
                    ImageContent => "[image]",
                    _ => string.Empty,
                };
                parts.Add(new JsonObject { ["text"] = text });
            }

            contents.Add(new JsonObject { ["role"] = role, ["parts"] = parts });
        }

        return contents;
    }

    private static JsonArray BuildAnthropicMessages(Conversation conversation)
    {
        var messages = new JsonArray();

        foreach (var message in conversation.Messages)
        {
            string role = message.Role == Role.User ? "user" : "assistant";

            if (message.Content.Count == 1 && message.Content[0] is TextContent single)
            {
                messages.Add(new JsonObject { ["role"] = role, ["content"] = single.Text });
                continue;
            }

            var contentArray = new JsonArray();
            foreach (var block in message.Content)
            {
                contentArray.Add(block switch
                {
                    TextContent text => (JsonNode)new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text.Text,
                    },
                    ToolCallContent call => new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["input"] = JsonNode.Parse(call.InputJson) ?? new JsonObject(),
                    },
                    ToolResultContent result => new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = result.ToolCallId,
                        ["content"] = result.Content,
                    },
                    _ => new JsonObject { ["type"] = "text", ["text"] = string.Empty },
                });
            }

            messages.Add(new JsonObject { ["role"] = role, ["content"] = contentArray });
        }

        return messages;
    }

    private static LlmErrorKind MapStatusCode(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => LlmErrorKind.Auth,
        HttpStatusCode.Forbidden => LlmErrorKind.Auth,
        HttpStatusCode.TooManyRequests => LlmErrorKind.RateLimit,
        HttpStatusCode.BadRequest => LlmErrorKind.InvalidRequest,
        HttpStatusCode.UnprocessableEntity => LlmErrorKind.InvalidRequest,
        _ => LlmErrorKind.Network,
    };
}
