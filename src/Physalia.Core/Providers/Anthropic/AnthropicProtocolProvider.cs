// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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

namespace Physalia.Core.Providers.Anthropic;

/// <summary>
/// Abstract base class for all providers that speak the Anthropic Messages wire format.
/// Handles HTTP transport, SSE parsing, and message serialisation.
/// Subclasses override <see cref="BuildRequestBody"/> to inject provider-specific parameters.
/// </summary>
public abstract class AnthropicProtocolProvider : ProtocolProviderBase<AnthropicProtocolConfig>
{
    private const string AnthropicVersion = "2023-06-01";

    // Thinking tokens share the max_tokens budget on this protocol, so the default must
    // leave room for reasoning AND a full GhJSON document. 8192 was measured too small in
    // live pipeline runs: adaptive thinking consumed it whole and the answer truncated
    // mid-JSON (or never started). Streaming means the higher ceiling has no timeout cost.
    private const int FallbackMaxTokens = 32768;

    // Default budget for the manual thinking form (adaptive intent mapped onto a
    // manual-budget-only model). Deliberately NOT tied to FallbackMaxTokens — the answer,
    // not the reasoning, should get the extra room.
    private const int DefaultManualThinkingBudget = 8192;

    private const int MinThinkingBudget = 1024;
    private const int ThinkingAnswerHeadroom = 4096;

    // Effort sent alongside adaptive thinking on models that accept output_config. The
    // server default is "high", which over-reasons in pipeline loops; "medium" tempers
    // thinking depth while keeping it visible. Explicit Tweaker thinking values still
    // control whether thinking runs at all.
    private const string AdaptiveThinkingEffort = "medium";

    /// <summary>
    /// Builds the JSON request body. Override in subclasses to inject provider-specific fields.
    /// </summary>
    /// <param name="conversation">The conversation history.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="config">The provider configuration.</param>
    /// <param name="tools">Tool definitions to advertise to the model, or null/empty to send none.</param>
    /// <returns>A <see cref="JsonObject"/> ready for serialisation.</returns>
    /// <remarks>
    /// Thinking and sampling parameters are shaped per model via
    /// <see cref="AnthropicModelDefaults"/>: a null <see cref="AnthropicProtocolConfig.ThinkingBudget"/>
    /// applies the model's known behaviour (models that think by default get summarized
    /// display so the billed thinking is visible), while explicit values are mapped to the
    /// thinking form the model actually accepts (adaptive-only generations reject the manual
    /// <c>enabled</c>/<c>budget_tokens</c> form and vice versa). Adaptive thinking is sent
    /// with <c>output_config: { effort: "medium" }</c> on models that accept it, tempering
    /// the server's "high" default so reasoning does not starve the answer of tokens.
    /// </remarks>
    protected virtual JsonObject BuildRequestBody(
        Conversation conversation,
        string systemPrompt,
        AnthropicProtocolConfig config,
        IReadOnlyList<LlmToolDefinition>? tools)
    {
        AnthropicModelDefaults.Entry model = AnthropicModelDefaults.Resolve(config.ModelId);

        // Decide the thinking config first — the manual form constrains max_tokens.
        // null = auto (model default), 0 = explicitly off, -1 = adaptive, >0 = manual budget.
        JsonObject? thinking = null;
        int? manualBudget = null;

        switch (config.ThinkingBudget)
        {
            case null:
                if (model.ThinkingOnByDefault)
                {
                    // These models think (and bill) regardless; summarized display is the
                    // only way to make that thinking visible instead of empty deltas.
                    thinking = AdaptiveThinking();
                }

                break;

            case 0:
                if (model.ThinkingOnByDefault && model.SupportsDisabled)
                {
                    thinking = new JsonObject { ["type"] = "disabled" };
                }

                // Models that are off by default need nothing; models that cannot be
                // disabled (Fable/Mythos) get no config — display stays omitted, which
                // is the closest available approximation of "off".
                break;

            case < 0:
                if (model.SupportsAdaptive)
                {
                    thinking = AdaptiveThinking();
                }
                else
                {
                    // Older generations have no adaptive form; honour the intent
                    // ("thinking on, visible") with a manual default budget.
                    manualBudget = DefaultManualThinkingBudget;
                }

                break;

            case int budget:
                if (model.SupportsManualBudget)
                {
                    manualBudget = Math.Max(budget, MinThinkingBudget);
                }
                else
                {
                    // Adaptive-only generations 400 on the manual form — map the intent.
                    thinking = AdaptiveThinking();
                }

                break;
        }

        // max_tokens is required by the Anthropic API.
        int maxTokens = config.MaxTokens > 0 ? config.MaxTokens : FallbackMaxTokens;

        if (manualBudget is int b)
        {
            if (maxTokens <= b)
            {
                // max_tokens must strictly exceed budget_tokens. Bump max_tokens rather
                // than shrinking the budget so the answer keeps headroom after thinking —
                // a budget that swallows the whole response yields an empty answer downstream.
                maxTokens = b + ThinkingAnswerHeadroom;
            }

            thinking = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = b,
            };
        }

        var body = new JsonObject
        {
            ["model"] = config.ModelId,
            ["max_tokens"] = maxTokens,
            ["stream"] = true,
            ["messages"] = BuildMessagesArray(conversation),
        };

        if (thinking is not null)
        {
            body["thinking"] = thinking;
        }

        // Adaptive thinking without an effort level runs at the server default ("high"),
        // which reasons far more than a canvas-editing loop needs and eats the shared
        // max_tokens budget. Temper it to "medium" on models that accept the field.
        if (model.SupportsEffort
            && thinking is not null
            && thinking["type"]!.GetValue<string>() == "adaptive")
        {
            body["output_config"] = new JsonObject { ["effort"] = AdaptiveThinkingEffort };
        }

        // Sampling parameters are omitted whenever thinking is active (extended thinking
        // requires temperature 1 and rejects top_k) and on model generations that reject
        // non-default sampling values on every request.
        bool thinkingActive = thinking is not null &&
            thinking["type"]!.GetValue<string>() != "disabled";

        if (!thinkingActive && model.AllowsSampling)
        {
            // Anthropic temperature is 0.0–1.0. Clamp values that come from a wider range.
            float temperature = Math.Min(Math.Max(config.Temperature, 0.0f), 1.0f);

            // Anthropic rejects requests that include both temperature and top_p.
            // top_p < 1.0 means the user has explicitly engaged nucleus sampling — use it
            // exclusively. Otherwise fall back to temperature.
            if (config.TopP < 1.0f)
            {
                body["top_p"] = config.TopP;
            }
            else
            {
                body["temperature"] = temperature;
            }

            // top_k is optional — omit when zero so the provider default applies.
            if (config.TopK > 0)
            {
                body["top_k"] = config.TopK;
            }
        }

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            body["system"] = systemPrompt;
        }

        if (tools is { Count: > 0 })
        {
            body["tools"] = BuildToolsArray(tools);
        }

        return body;
    }

    /// <summary>
    /// Builds the adaptive thinking config with summarized display — the form the newest
    /// generations require to return readable thinking text instead of empty deltas.
    /// </summary>
    /// <returns>The <c>thinking</c> object for the request body.</returns>
    private static JsonObject AdaptiveThinking() => new()
    {
        ["type"] = "adaptive",
        ["display"] = "summarized",
    };

    /// <summary>
    /// Serialises tool definitions into the Anthropic <c>tools</c> array
    /// (<c>{ name, description, input_schema }</c> per tool).
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
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = ParseToolSchema(tool.InputSchemaJson),
            });
        }

        return array;
    }

    /// <inheritdoc/>
    protected override async Task<Result<IReadOnlyList<string>, LlmError>> FetchAvailableModelsAsync(
        AnthropicProtocolConfig config,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{config.BaseUrl}/models");
        request.Headers.Add("x-api-key", config.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);

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
        string systemPrompt,
        AnthropicProtocolConfig config,
        IReadOnlyList<LlmToolDefinition>? tools,
        CancellationToken ct)
    {
        var body = BuildRequestBody(conversation, systemPrompt, config, tools);
        var json = body.ToJsonString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl}/messages");
        request.Headers.Add("x-api-key", config.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return await SendStreamingRequestAsync(request, ct);
    }

    /// <summary>
    /// Parses the Anthropic SSE stream. Each event block is delimited by a blank line and
    /// carries both an "event:" type line and a "data:" JSON payload.
    /// Tracks content_block_start/delta/stop events to accumulate tool call arguments.
    /// </summary>
    /// <inheritdoc/>
    protected override async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> ParseSseStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        string currentEventType = string.Empty;
        string currentData = string.Empty;
        int inputTokens = 0;
        bool done = false;

        // Tool call accumulation across content_block_* events.
        string? pendingToolId = null;
        string? pendingToolName = null;
        StringBuilder? pendingToolArgs = null;
        var completedToolCalls = new List<LlmToolCall>();

        // Thinking blocks are re-emitted inline as <think>…</think> so the payload carries
        // them. The tag opens lazily on the first non-empty thinking delta, so a thinking
        // block that streams no text emits no tags at all.
        bool inThinkingBlock = false;
        bool thinkingTagOpen = false;

        while (!ct.IsCancellationRequested && !done)
        {
            (string? line, LlmError? readError) = await ReadStreamLineAsync(reader);

            if (readError != null)
            {
                yield return new Result<LlmResponseChunk, LlmError>.Err(readError);
                yield break;
            }

            if (line == null) break;

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEventType = line.Substring(7);
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                currentData = line.Substring(6);
                continue;
            }

            // Blank line signals end of event block.
            if (line.Length != 0 || currentData.Length == 0) continue;

            string eventType = currentEventType;
            string data = currentData;
            currentEventType = string.Empty;
            currentData = string.Empty;

            Result<LlmResponseChunk, LlmError>? chunk = null;
            Exception? parseError = null;

            try
            {
                switch (eventType)
                {
                    case "message_start":
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("message", out var msg) &&
                            msg.TryGetProperty("usage", out var usage) &&
                            usage.TryGetProperty("input_tokens", out var it))
                        {
                            inputTokens = it.GetInt32();
                        }

                        break;
                    }

                    case "content_block_start":
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("content_block", out var cb) &&
                            cb.TryGetProperty("type", out var typeEl))
                        {
                            string blockType = typeEl.GetString() ?? string.Empty;

                            if (blockType == "tool_use")
                            {
                                pendingToolId = cb.TryGetProperty("id", out var idEl)
                                    ? idEl.GetString() ?? string.Empty
                                    : string.Empty;
                                pendingToolName = cb.TryGetProperty("name", out var nameEl)
                                    ? nameEl.GetString() ?? string.Empty
                                    : string.Empty;
                                pendingToolArgs = new StringBuilder();
                            }
                            else if (blockType == "thinking")
                            {
                                inThinkingBlock = true;
                            }

                            // redacted_thinking blocks carry no readable deltas — ignored.
                        }

                        break;
                    }

                    case "content_block_delta":
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("type", out var typeEl))
                        {
                            string deltaType = typeEl.GetString() ?? string.Empty;

                            if (deltaType == "text_delta" &&
                                delta.TryGetProperty("text", out var text))
                            {
                                chunk = new Result<LlmResponseChunk, LlmError>.Ok(
                                    new LlmResponseChunk(text.GetString(), false, null));
                            }
                            else if (deltaType == "thinking_delta" &&
                                delta.TryGetProperty("thinking", out var thinking))
                            {
                                string thinkingText = thinking.GetString() ?? string.Empty;
                                if (thinkingText.Length > 0)
                                {
                                    string prefix = thinkingTagOpen ? string.Empty : ThinkingTags.Open;
                                    thinkingTagOpen = true;
                                    chunk = new Result<LlmResponseChunk, LlmError>.Ok(
                                        new LlmResponseChunk(prefix + thinkingText, false, null));
                                }
                            }
                            else if (deltaType == "input_json_delta" &&
                                delta.TryGetProperty("partial_json", out var partial) &&
                                pendingToolArgs != null)
                            {
                                pendingToolArgs.Append(partial.GetString());
                            }

                            // signature_delta is the opaque replay signature for thinking
                            // blocks — inline tags cannot carry it, so it is skipped.
                        }

                        break;
                    }

                    case "content_block_stop":
                    {
                        if (inThinkingBlock)
                        {
                            inThinkingBlock = false;
                            if (thinkingTagOpen)
                            {
                                thinkingTagOpen = false;
                                chunk = new Result<LlmResponseChunk, LlmError>.Ok(
                                    new LlmResponseChunk(ThinkingTags.CloseAndSeparate, false, null));
                            }
                        }

                        if (pendingToolId != null && pendingToolArgs != null)
                        {
                            completedToolCalls.Add(new LlmToolCall(
                                pendingToolId,
                                pendingToolName ?? string.Empty,
                                pendingToolArgs.ToString()));
                            pendingToolId = null;
                            pendingToolName = null;
                            pendingToolArgs = null;
                        }

                        break;
                    }

                    case "message_delta":
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;
                        int outputTokens = 0;
                        if (root.TryGetProperty("usage", out var usage) &&
                            usage.TryGetProperty("output_tokens", out var ot))
                        {
                            outputTokens = ot.GetInt32();
                        }

                        string? stopReason = null;
                        if (root.TryGetProperty("delta", out var messageDelta) &&
                            messageDelta.TryGetProperty("stop_reason", out var sr) &&
                            sr.ValueKind == JsonValueKind.String)
                        {
                            stopReason = sr.GetString();
                        }

                        IReadOnlyList<LlmToolCall>? toolCalls = completedToolCalls.Count > 0
                            ? completedToolCalls
                            : null;

                        // A stream cut mid-thinking (e.g. at max_tokens) still closes the tag.
                        string? finalDelta = thinkingTagOpen ? ThinkingTags.Close : null;
                        thinkingTagOpen = false;

                        chunk = new Result<LlmResponseChunk, LlmError>.Ok(
                            new LlmResponseChunk(finalDelta, true, new LlmUsage(inputTokens, outputTokens), toolCalls, stopReason));
                        done = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                parseError = ex;
            }

            if (parseError != null)
            {
                yield return new Result<LlmResponseChunk, LlmError>.Err(
                    new LlmError(LlmErrorKind.InvalidRequest, $"Failed to parse event '{eventType}': {parseError.Message}"));
                yield break;
            }

            if (chunk is not null)
            {
                yield return chunk;
            }
        }

        if (ct.IsCancellationRequested)
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Stream was cancelled."));
        }
    }

    private static JsonArray BuildMessagesArray(Conversation conversation)
    {
        var messages = new JsonArray();

        foreach (var message in conversation.Messages)
        {
            // Thinking is display-only — assistant history is resent without <think> blocks.
            var outbound = message.Role == Role.Assistant
                ? ThinkingTags.StripAssistantMessage(message)
                : message;
            messages.Add(BuildMessage(outbound));
        }

        return messages;
    }

    private static JsonNode BuildMessage(ConversationMessage message)
    {
        var role = message.Role == Role.User ? "user" : "assistant";

        // Single text block: use the compact string form.
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
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = img.MimeType,
                    ["data"] = Convert.ToBase64String(img.Data),
                },
            },

            ImageContent { Source: UrlImage url } => new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "url",
                    ["url"] = url.Url,
                },
            },

            ImageContent { Source: ManagedImage managed } => new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "file",
                    ["file_id"] = managed.FileHandle,
                },
            },

            // Anthropic uses tool_use / tool_result content block types.
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
                ["is_error"] = result.IsError,
            },

            _ => throw new InvalidOperationException(
                    $"Unsupported content block type: {block.GetType().Name}."),
        };
    }
}
