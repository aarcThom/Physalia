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

namespace Physalia.Core.Providers.Gemini;

/// <summary>
/// Abstract base class for all providers that speak the Google Gemini GenerateContent wire format.
/// Handles HTTP transport, SSE parsing, and message serialisation.
/// Subclasses override <see cref="BuildGenerationConfig"/> to inject provider-specific parameters.
/// </summary>
public abstract class GeminiProtocolProvider : ProtocolProviderBase<GeminiProtocolConfig>
{
    /// <summary>
    /// Returns the list of chat-capable model IDs available on the Gemini API.
    /// Only models that support <c>generateContent</c> are included.
    /// </summary>
    /// <param name="config">The already-downcast provider configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of model ID strings, or an error.</returns>
    protected override async Task<Result<IReadOnlyList<string>, LlmError>> FetchAvailableModelsAsync(
        GeminiProtocolConfig config,
        CancellationToken ct)
    {
        string url = $"{config.BaseUrl}/models?key={config.ApiKey}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var bodyResult = await SendForStringAsync(request, ct);
        if (bodyResult.IsErr(out var bodyErr, out var json))
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(bodyErr);
        }

        using var doc = JsonDocument.Parse(json);

        var ids = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var models))
        {
            foreach (var model in models.EnumerateArray())
            {
                // Only include models that support generateContent (i.e. chat/text generation).
                if (!SupportsGenerateContent(model)) continue;

                if (model.TryGetProperty("name", out var nameEl))
                {
                    // Strip the "models/" prefix — the API returns e.g. "models/gemini-2.0-flash".
                    string fullName = nameEl.GetString() ?? string.Empty;
                    string id = fullName.StartsWith("models/", StringComparison.Ordinal)
                        ? fullName.Substring(7)
                        : fullName;

                    if (!string.IsNullOrEmpty(id))
                        ids.Add(id);
                }
            }
        }

        return new Result<IReadOnlyList<string>, LlmError>.Ok(ids);
    }

    /// <summary>
    /// Builds the <c>generationConfig</c> object for the request body.
    /// Override to add or modify provider-specific fields.
    /// </summary>
    /// <param name="config">The provider configuration.</param>
    /// <returns>A <see cref="JsonObject"/> for the <c>generationConfig</c> field.</returns>
    protected virtual JsonObject BuildGenerationConfig(GeminiProtocolConfig config)
    {
        var cfg = new JsonObject
        {
            ["temperature"] = config.Temperature,
            ["topP"] = config.TopP,
            ["maxOutputTokens"] = config.MaxTokens,
        };

        // topK is optional — omit when zero so the provider default applies.
        if (config.TopK > 0)
        {
            cfg["topK"] = config.TopK;
        }

        // includeThoughts streams thought summaries so the inline <think> wrapping has
        // content to carry. An explicit budget always sends it; otherwise the model's
        // known behaviour decides (thinking-capable models think by default but return
        // no thought text unless asked; older models reject thinkingConfig entirely).
        if (config.ThinkingBudget is int thinkingBudget)
        {
            cfg["thinkingConfig"] = new JsonObject
            {
                ["thinkingBudget"] = thinkingBudget,
                ["includeThoughts"] = true,
            };
        }
        else if (GeminiModelDefaults.Resolve(config.ModelId).IncludeThoughtsByDefault)
        {
            cfg["thinkingConfig"] = new JsonObject
            {
                ["includeThoughts"] = true,
            };
        }

        return cfg;
    }

    // HELPERS =====================================================================================

    /// <inheritdoc/>
    protected override async Task<Result<HttpResponseMessage, LlmError>> SendHttpRequestAsync(
        Conversation conversation,
        SystemPrompt systemPrompt,
        GeminiProtocolConfig config,
        IReadOnlyList<LlmToolDefinition>? tools,
        CancellationToken ct)
    {
        var body = BuildRequestBody(conversation, systemPrompt, config, tools);
        var json = body.ToJsonString();

        // API key is passed as a query parameter; alt=sse requests SSE-formatted streaming.
        string url = $"{config.BaseUrl}/models/{config.ModelId}:streamGenerateContent?key={config.ApiKey}&alt=sse";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return await SendStreamingRequestAsync(request, ct);
    }

    private JsonObject BuildRequestBody(
        Conversation conversation,
        SystemPrompt systemPrompt,
        GeminiProtocolConfig config,
        IReadOnlyList<LlmToolDefinition>? tools)
    {
        var body = new JsonObject
        {
            ["contents"] = BuildContents(conversation),
            ["generationConfig"] = BuildGenerationConfig(config),
        };

        if (!systemPrompt.IsEmpty)
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = systemPrompt.Text },
                },
            };
        }

        if (tools is { Count: > 0 })
        {
            body["tools"] = BuildToolsArray(tools);
        }

        return body;
    }

    /// <summary>
    /// Serialises tool definitions into the Gemini <c>tools</c> array — a single entry whose
    /// <c>functionDeclarations</c> lists each tool's <c>{ name, description, parameters }</c>.
    /// </summary>
    /// <param name="tools">The tool definitions to serialise.</param>
    /// <returns>A JSON array for the request body's <c>tools</c> field.</returns>
    private static JsonArray BuildToolsArray(IReadOnlyList<LlmToolDefinition> tools)
    {
        var declarations = new JsonArray();
        foreach (LlmToolDefinition tool in tools)
        {
            declarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = ParseToolSchema(tool.InputSchemaJson),
            });
        }

        return new JsonArray
        {
            new JsonObject { ["functionDeclarations"] = declarations },
        };
    }

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> ParseSseStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        // Thought parts (thought:true) are re-emitted inline as <think>…</think>; the flag
        // spans chunks so the tag opens once and closes on the first non-thought text part.
        bool inThinking = false;

        while (!ct.IsCancellationRequested)
        {
            (string? line, LlmError? readError) = await ReadStreamLineAsync(reader);

            if (readError != null)
            {
                yield return new Result<LlmResponseChunk, LlmError>.Err(readError);
                yield break;
            }

            if (line == null) break; // Stream closed — normal end for Gemini (no [DONE] marker).
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
                // `candidates`. With no check here the payload matched nothing, the stream ended,
                // and the caller treated the partial text as a COMPLETE successful response.
                if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Object)
                {
                    string status = errorEl.TryGetProperty("status", out var se) && se.ValueKind == JsonValueKind.String
                        ? se.GetString() ?? string.Empty
                        : string.Empty;
                    string message = errorEl.TryGetProperty("message", out var me) && me.ValueKind == JsonValueKind.String
                        ? me.GetString() ?? string.Empty
                        : string.Empty;

                    if (message.Length == 0)
                    {
                        message = "The provider reported an error mid-stream.";
                    }

                    streamError = new LlmError(
                        HttpErrorMapper.MapErrorType(status),
                        status.Length == 0 ? message : $"{status} — {message}");
                }

                string? contentDelta = null;
                bool isLast = false;
                string? stopReason = null;
                LlmUsage? usage = null;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];

                    // Text delta — concatenate all text parts in this chunk, wrapping
                    // thought parts (thought:true) in inline thinking tags.
                    if (candidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (!part.TryGetProperty("text", out var textEl) ||
                                textEl.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            bool isThought = part.TryGetProperty("thought", out var thoughtEl) &&
                                thoughtEl.ValueKind == JsonValueKind.True;

                            if (isThought && !inThinking)
                            {
                                sb.Append(ThinkingTags.Open);
                                inThinking = true;
                            }
                            else if (!isThought && inThinking)
                            {
                                sb.Append(ThinkingTags.CloseAndSeparate);
                                inThinking = false;
                            }

                            sb.Append(textEl.GetString());
                        }

                        string text = sb.ToString();
                        if (text.Length > 0) contentDelta = text;
                    }

                    // A non-null finishReason signals the last chunk.
                    if (candidate.TryGetProperty("finishReason", out var finishReasonEl) &&
                        finishReasonEl.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(finishReasonEl.GetString()))
                    {
                        isLast = true;
                        stopReason = finishReasonEl.GetString();

                        // A stream cut while still thinking (e.g. MAX_TOKENS) closes the tag.
                        if (inThinking)
                        {
                            contentDelta = (contentDelta ?? string.Empty) + ThinkingTags.Close;
                            inThinking = false;
                        }
                    }
                }

                // Usage metadata arrives on the final chunk.
                if (isLast && root.TryGetProperty("usageMetadata", out var usageMeta))
                {
                    int inputTokens = usageMeta.TryGetProperty("promptTokenCount", out var ptc)
                        ? ptc.GetInt32() : 0;
                    int outputTokens = usageMeta.TryGetProperty("candidatesTokenCount", out var ctc)
                        ? ctc.GetInt32() : 0;
                    usage = new LlmUsage(inputTokens, outputTokens);
                }

                if (contentDelta != null || isLast)
                {
                    parsed = new Result<LlmResponseChunk, LlmError>.Ok(
                        new LlmResponseChunk(contentDelta, isLast, usage, null, stopReason));
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

    private static JsonArray BuildContents(Conversation conversation)
    {
        var contents = new JsonArray();

        // Gemini pairs a functionResponse to its call by FUNCTION NAME, not by call id — it has no
        // id field at all. ToolResultContent carries only the id, so the name has to be recovered
        // from the call that asked, which always precedes the result in the same conversation.
        var toolNamesById = new Dictionary<string, string>();

        foreach (var inbound in conversation.Messages)
        {
            // Thinking is display-only — assistant history is resent without <think> blocks.
            var message = inbound.Role == Role.Assistant
                ? ThinkingTags.StripAssistantMessage(inbound)
                : inbound;

            // Gemini uses "model" for the assistant role, not "assistant".
            string role = message.Role == Role.User ? "user" : "model";

            var parts = new JsonArray();
            foreach (var block in message.Content)
            {
                if (block is ToolCallContent call)
                {
                    toolNamesById[call.Id] = call.Name;
                }

                parts.Add(BuildPart(block, toolNamesById));
            }

            contents.Add(new JsonObject { ["role"] = role, ["parts"] = parts });
        }

        return contents;
    }

    private static JsonNode BuildPart(MessageContent block, IReadOnlyDictionary<string, string> toolNamesById)
    {
        return block switch
        {
            TextContent text => new JsonObject { ["text"] = text.Text },

            ImageContent { Source: InlineImage img } => new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = img.MimeType,
                    ["data"] = Convert.ToBase64String(img.Data),
                },
            },

            // Gemini managed images are GCS paths or Files API URIs.
            ImageContent { Source: ManagedImage managed } => new JsonObject
            {
                ["fileData"] = new JsonObject
                {
                    ["fileUri"] = managed.FileHandle,
                },
            },

            ImageContent { Source: UrlImage } =>
                throw new InvalidOperationException(
                    "Gemini does not support arbitrary public URL images. Use InlineImage or ManagedImage with a GCS/Files API URI."),

            ToolCallContent call => new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["name"] = call.Name,
                    ["args"] = ParseToolInputOrEmpty(call.InputJson),
                },
            },

            // Falls back to the id only when the asking call is not in the conversation — a
            // pairing Gemini will reject, but Reassemble strips that orphan before it gets here.
            ToolResultContent result => new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = toolNamesById.TryGetValue(result.ToolCallId, out string? toolName)
                        ? toolName
                        : result.ToolCallId,
                    ["response"] = new JsonObject { ["content"] = result.Content },
                },
            },

            _ => throw new InvalidOperationException(
                    $"Unsupported content block type: {block.GetType().Name}."),
        };
    }

    private static bool SupportsGenerateContent(JsonElement model)
    {
        if (!model.TryGetProperty("supportedGenerationMethods", out var methods))
            return false;

        foreach (var method in methods.EnumerateArray())
        {
            if (method.ValueKind == JsonValueKind.String &&
                method.GetString() == "generateContent")
            {
                return true;
            }
        }

        return false;
    }
}
