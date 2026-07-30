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
using Physalia.Core.Models.Defaults;
using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Providers.OpenAiProtocol;

/// <summary>
/// Abstract base class for all providers that speak the OpenAI Chat Completions wire format.
/// Implements HTTP transport, SSE parsing, and message serialisation once.
/// Subclasses override <see cref="BuildRequestBody"/> to inject provider-specific parameters.
/// </summary>
public abstract class OpenAIProtocolProvider : ProtocolProviderBase<OpenAIProtocolConfig>
{

    /// <summary>
    /// Builds the JSON request body. Override in subclasses to add or strip provider-specific fields
    /// (e.g. DeepSeek thinking mode, Ollama keep_alive, OpenRouter provider routing).
    /// </summary>
    /// <param name="conversation">The conversation history.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="config">The provider configuration.</param>
    /// <param name="tools">Tool definitions to advertise to the model, or null/empty to send none.</param>
    /// <returns>A <see cref="JsonObject"/> ready for serialisation.</returns>
    protected virtual JsonObject BuildRequestBody(
        Conversation conversation,
        SystemPrompt systemPrompt,
        OpenAIProtocolConfig config,
        IReadOnlyList<LlmToolDefinition>? tools)
    {
        OpenAIModelDefaults.Entry model = OpenAIModelDefaults.Resolve(config.ModelId);

        var body = new JsonObject
        {
            ["model"] = config.ModelId,
            ["stream"] = true,
            ["messages"] = BuildMessagesArray(conversation, systemPrompt),
        };

        // OpenAI reasoning models (o-series, GPT-5) reject the deprecated max_tokens
        // and require max_completion_tokens instead.
        body[model.UsesMaxCompletionTokens ? "max_completion_tokens" : "max_tokens"] = config.MaxTokens;

        // Reasoning models also reject sampling parameters.
        if (model.AllowsSampling)
        {
            body["temperature"] = config.Temperature;
            body["top_p"] = config.TopP;
        }

        // reasoning_effort is only understood by reasoning-capable models/servers — omit otherwise.
        if (!string.IsNullOrWhiteSpace(config.ReasoningEffort))
        {
            body["reasoning_effort"] = config.ReasoningEffort;
        }

        // DeepSeek V4 emits reasoning_content only when thinking is requested explicitly.
        // null = auto (apply the model's known behaviour); other OpenAI-compatible servers
        // may reject the unknown field, so the fallback keeps it omitted.
        if (config.ThinkingEnabled ?? model.ThinkingOnByDefault)
        {
            body["thinking"] = new JsonObject { ["type"] = "enabled" };
        }

        if (tools is { Count: > 0 })
        {
            body["tools"] = BuildToolsArray(tools);
        }

        return body;
    }

    /// <summary>
    /// Serialises tool definitions into the OpenAI <c>tools</c> array
    /// (<c>{ type: "function", function: { name, description, parameters } }</c> per tool).
    /// </summary>
    /// <param name="tools">The tool definitions to serialise.</param>
    /// <returns>A JSON array for the request body's <c>tools</c> field.</returns>
    private static JsonArray BuildToolsArray(IReadOnlyList<LlmToolDefinition> tools)
    {
        var array = new JsonArray();
        foreach (LlmToolDefinition tool in tools)
        {
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = ParseToolSchema(tool.InputSchemaJson),
                },
            });
        }

        return array;
    }

    /// <inheritdoc/>
    /// <remarks>For llama-server this will be a single entry — the model currently loaded.</remarks>
    protected override async Task<Result<IReadOnlyList<string>, LlmError>> FetchAvailableModelsAsync(
        OpenAIProtocolConfig config,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{config.BaseUrl}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        var bodyResult = await SendForStringAsync(request, ct);
        if (bodyResult.IsErr(out var bodyErr, out var json))
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(bodyErr);
        }

        return new Result<IReadOnlyList<string>, LlmError>.Ok(ParseModelIdsFromDataArray(json));
    }

    /// <inheritdoc/>
    protected override async Task<Result<HttpResponseMessage, LlmError>> SendHttpRequestAsync(
        Conversation conversation,
        SystemPrompt systemPrompt,
        OpenAIProtocolConfig config,
        IReadOnlyList<LlmToolDefinition>? tools,
        CancellationToken ct)
    {
        var body = BuildRequestBody(conversation, systemPrompt, config, tools);
        var json = body.ToJsonString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return await SendStreamingRequestAsync(request, ct);
    }

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> ParseSseStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        // Tool call argument accumulation keyed by the index field in each delta.
        var toolCallBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        // Reasoning deltas (DeepSeek reasoning_content / OpenRouter reasoning) are re-emitted
        // inline as <think>…</think>; the flag spans chunks so the tag opens once and closes
        // on the first visible content (or the final chunk).
        bool inReasoning = false;

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
            LlmError? streamError = null;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // A mid-stream failure arrives as a data payload carrying `error` instead of
                // `choices` (OpenAI, OpenRouter, Ollama and vLLM all do this). With no check here
                // the payload matched nothing, the stream ended, and the caller treated the
                // partial text as a COMPLETE successful response.
                if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Object)
                {
                    string errorType = errorEl.TryGetProperty("type", out var te) && te.ValueKind == JsonValueKind.String
                        ? te.GetString() ?? string.Empty
                        : string.Empty;
                    string message = errorEl.TryGetProperty("message", out var me) && me.ValueKind == JsonValueKind.String
                        ? me.GetString() ?? string.Empty
                        : string.Empty;

                    if (message.Length == 0)
                    {
                        message = "The provider reported an error mid-stream.";
                    }

                    streamError = new LlmError(
                        HttpErrorMapper.MapErrorType(errorType),
                        errorType.Length == 0 ? message : $"{errorType} — {message}");
                }

                string? contentDelta = null;
                bool isLast = false;
                string? stopReason = null;
                IReadOnlyList<LlmToolCall>? toolCalls = null;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];

                    if (choice.TryGetProperty("delta", out var delta))
                    {
                        // Reasoning delta — DeepSeek uses reasoning_content, OpenRouter uses reasoning.
                        string? reasoningDelta = null;
                        if (delta.TryGetProperty("reasoning_content", out var rc) &&
                            rc.ValueKind == JsonValueKind.String)
                        {
                            reasoningDelta = rc.GetString();
                        }
                        else if (delta.TryGetProperty("reasoning", out var r) &&
                            r.ValueKind == JsonValueKind.String)
                        {
                            reasoningDelta = r.GetString();
                        }

                        // Text delta.
                        string? visibleDelta = null;
                        if (delta.TryGetProperty("content", out var content) &&
                            content.ValueKind == JsonValueKind.String)
                        {
                            visibleDelta = content.GetString();
                        }

                        if (!string.IsNullOrEmpty(reasoningDelta) || !string.IsNullOrEmpty(visibleDelta))
                        {
                            var composed = new StringBuilder();
                            if (!string.IsNullOrEmpty(reasoningDelta))
                            {
                                if (!inReasoning)
                                {
                                    composed.Append(ThinkingTags.Open);
                                    inReasoning = true;
                                }

                                composed.Append(reasoningDelta);
                            }

                            if (!string.IsNullOrEmpty(visibleDelta))
                            {
                                if (inReasoning)
                                {
                                    composed.Append(ThinkingTags.CloseAndSeparate);
                                    inReasoning = false;
                                }

                                composed.Append(visibleDelta);
                            }

                            contentDelta = composed.ToString();
                        }
                        else
                        {
                            // Null or empty content (DeepSeek streams content:""/null while
                            // reasoning) never closes the tag; the pre-reasoning behaviour
                            // of yielding an empty-string content chunk is preserved.
                            contentDelta = visibleDelta;
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
                        stopReason = finishReason.GetString();

                        // A stream cut while still reasoning (e.g. finish_reason "length")
                        // closes the tag on this final chunk.
                        if (inReasoning)
                        {
                            contentDelta = (contentDelta ?? string.Empty) + ThinkingTags.Close;
                            inReasoning = false;
                        }

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
                        new LlmResponseChunk(contentDelta, isLast, null, toolCalls, stopReason));
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

            if (streamError != null)
            {
                yield return new Result<LlmResponseChunk, LlmError>.Err(streamError);
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

    private static JsonArray BuildMessagesArray(Conversation conversation, SystemPrompt systemPrompt)
    {
        var messages = new JsonArray();

        // Segments arrive stable-first, which is all OpenAI's automatic prefix caching needs:
        // an unchanged leading span of the request hits cache without any explicit marker.
        if (!systemPrompt.IsEmpty)
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = systemPrompt.Text });
        }

        foreach (var inbound in conversation.Messages)
        {
            // Thinking is display-only — assistant history is resent without <think> blocks.
            var message = inbound.Role == Role.Assistant
                ? ThinkingTags.StripAssistantMessage(inbound)
                : inbound;

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
