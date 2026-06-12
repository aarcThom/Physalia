// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Providers.OpenAiProtocol;

/// <summary>
/// Abstract base class for all providers that speak the OpenAI Chat Completions wire format.
/// Implements HTTP transport, SSE parsing, and message serialisation once.
/// Subclasses override <see cref="BuildRequestBody"/> to inject provider-specific parameters.
/// </summary>
public abstract class OpenAIProtocolProvider : ProtocolProviderBase
{
    /// <summary>
    /// Streams an inference call to the provider and yields response chunks as they arrive.
    /// </summary>
    /// <param name="conversation">The conversation history to send.</param>
    /// <param name="systemPrompt">The system prompt, passed at call time and not stored in the conversation.</param>
    /// <param name="config">Provider configuration. Must be an <see cref="OpenAIProtocolConfig"/> instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of result chunks.</returns>
    public override async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
        Conversation conversation,
        string systemPrompt,
        ModelConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(config);

        if (!TryGetConfig(config, out OpenAIProtocolConfig openAIConfig, out LlmError configError))
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(configError);
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

    /// <summary>
    /// Returns the list of model IDs available at the configured endpoint.
    /// For llama-server this will be a single entry — the model currently loaded.
    /// </summary>
    /// <param name="config">Provider configuration. Must be an <see cref="OpenAIProtocolConfig"/> instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of model ID strings, or an error.</returns>
    public override async Task<Result<IReadOnlyList<string>, LlmError>> GetAvailableModelsAsync(
        ModelConfig config,
        CancellationToken ct)
    {
        if (!TryGetConfig(config, out OpenAIProtocolConfig openAIConfig, out LlmError configError))
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(configError);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{openAIConfig.BaseUrl}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAIConfig.ApiKey);

        var bodyResult = await SendForStringAsync(request, ct);
        if (bodyResult is Result<string, LlmError>.Err bodyErr)
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(bodyErr.Error);
        }

        var json = ((Result<string, LlmError>.Ok)bodyResult).Value;
        return new Result<IReadOnlyList<string>, LlmError>.Ok(ParseModelIdsFromDataArray(json));
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

        return await SendStreamingRequestAsync(request, ct);
    }

    private static async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> ParseSseStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        // Tool call argument accumulation keyed by the index field in each delta.
        var toolCallBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        while (!ct.IsCancellationRequested)
        {
            (string? line, LlmError? readError) = await ReadStreamLineAsync(reader);

            if (readError != null)
            {
                yield return new Result<LlmResponseChunk, LlmError>.Err(readError);
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
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                string? contentDelta = null;
                bool isLast = false;
                IReadOnlyList<LlmToolCall>? toolCalls = null;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];

                    if (choice.TryGetProperty("delta", out var delta))
                    {
                        // Text delta.
                        if (delta.TryGetProperty("content", out var content) &&
                            content.ValueKind == JsonValueKind.String)
                        {
                            contentDelta = content.GetString();
                        }

                        // Tool call argument deltas — accumulate by index.
                        if (delta.TryGetProperty("tool_calls", out var tcArray))
                        {
                            foreach (var tc in tcArray.EnumerateArray())
                            {
                                int index = tc.TryGetProperty("index", out var indexEl)
                                    ? indexEl.GetInt32() : 0;

                                if (!toolCallBuilders.ContainsKey(index))
                                {
                                    string id = tc.TryGetProperty("id", out var idEl)
                                        ? idEl.GetString() ?? string.Empty : string.Empty;
                                    string name = tc.TryGetProperty("function", out var fn) &&
                                        fn.TryGetProperty("name", out var nameEl)
                                        ? nameEl.GetString() ?? string.Empty : string.Empty;
                                    toolCallBuilders[index] = (id, name, new StringBuilder());
                                }

                                // Append argument fragment. StringBuilder is a reference type so
                                // indexing the dictionary twice is safe — both hits share the object.
                                if (tc.TryGetProperty("function", out var funcDelta) &&
                                    funcDelta.TryGetProperty("arguments", out var args) &&
                                    args.ValueKind == JsonValueKind.String)
                                {
                                    toolCallBuilders[index].Arguments.Append(args.GetString());
                                }
                            }
                        }
                    }

                    if (choice.TryGetProperty("finish_reason", out var finishReason) &&
                        finishReason.ValueKind != JsonValueKind.Null)
                    {
                        isLast = true;

                        if (toolCallBuilders.Count > 0)
                        {
                            var calls = new List<LlmToolCall>(toolCallBuilders.Count);
                            for (int i = 0; i < toolCallBuilders.Count; i++)
                            {
                                if (toolCallBuilders.TryGetValue(i, out var b))
                                {
                                    calls.Add(new LlmToolCall(b.Id, b.Name, b.Arguments.ToString()));
                                }
                            }

                            toolCalls = calls;
                        }
                    }
                }

                if (contentDelta != null || isLast)
                {
                    parsed = new Result<LlmResponseChunk, LlmError>.Ok(
                        new LlmResponseChunk(contentDelta, isLast, null, toolCalls));
                }
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

    private static JsonArray BuildMessagesArray(Conversation conversation, string systemPrompt)
    {
        var messages = new JsonArray();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = systemPrompt });
        }

        foreach (var message in conversation.Messages)
        {
            if (message.Role == Role.Assistant)
            {
                // Separate tool calls from other content — tool calls go in tool_calls[], not content[].
                var toolCalls = new List<ToolCallContent>();
                var otherContent = new List<MessageContent>();

                foreach (var block in message.Content)
                {
                    if (block is ToolCallContent tc) toolCalls.Add(tc);
                    else otherContent.Add(block);
                }

                if (toolCalls.Count > 0)
                {
                    var toolCallsArray = new JsonArray();
                    foreach (var call in toolCalls)
                    {
                        toolCallsArray.Add(new JsonObject
                        {
                            ["id"] = call.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = call.Name,
                                ["arguments"] = call.InputJson,
                            },
                        });
                    }

                    var assistantMsg = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["tool_calls"] = toolCallsArray,
                    };

                    if (otherContent.Count == 1 && otherContent[0] is TextContent singleText)
                    {
                        assistantMsg["content"] = singleText.Text;
                    }
                    else if (otherContent.Count > 0)
                    {
                        var contentArray = new JsonArray();
                        foreach (var block in otherContent) contentArray.Add(BuildContentBlock(block));
                        assistantMsg["content"] = contentArray;
                    }

                    messages.Add(assistantMsg);
                }
                else
                {
                    messages.Add(BuildMessage(message));
                }
            }
            else
            {
                // User messages: tool results become separate role:tool messages.
                var toolResults = new List<ToolResultContent>();
                var regularContent = new List<MessageContent>();

                foreach (var block in message.Content)
                {
                    if (block is ToolResultContent tr) toolResults.Add(tr);
                    else regularContent.Add(block);
                }

                foreach (var result in toolResults)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = result.ToolCallId,
                        ["content"] = result.Content,
                    });
                }

                if (regularContent.Count == 1 && regularContent[0] is TextContent singleText)
                {
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = singleText.Text });
                }
                else if (regularContent.Count > 0)
                {
                    var contentArray = new JsonArray();
                    foreach (var block in regularContent) contentArray.Add(BuildContentBlock(block));
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = contentArray });
                }
            }
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

            // Tool calls and results are handled at the message level, not as content blocks.
            ToolCallContent _ =>
                throw new InvalidOperationException(
                    "ToolCallContent must be serialised via the tool_calls message field, not as a content block."),

            ToolResultContent _ =>
                throw new InvalidOperationException(
                    "ToolResultContent must be serialised as a role:tool message, not as a content block."),

            _ => throw new InvalidOperationException(
                    $"Unsupported content block type: {block.GetType().Name}."),
        };
    }
}
