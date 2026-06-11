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
using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Providers.Gemini;

/// <summary>
/// Abstract base class for all providers that speak the Google Gemini GenerateContent wire format.
/// Handles HTTP transport, SSE parsing, and message serialisation.
/// Subclasses override <see cref="BuildGenerationConfig"/> to inject provider-specific parameters.
/// </summary>
public abstract class GeminiProtocolProvider : ProtocolProviderBase
{
    /// <summary>
    /// Streams an inference call to the Gemini <c>streamGenerateContent</c> endpoint and yields
    /// response chunks as they arrive.
    /// </summary>
    /// <param name="conversation">The conversation history to send.</param>
    /// <param name="systemPrompt">The system prompt, passed at call time and not stored in the conversation.</param>
    /// <param name="config">Provider configuration. Must be a <see cref="GeminiProtocolConfig"/> instance.</param>
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

        if (!TryGetConfig(config, out GeminiProtocolConfig geminiConfig, out LlmError configError))
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(configError);
            yield break;
        }

        var httpResult = await SendHttpRequestAsync(conversation, systemPrompt, geminiConfig, ct);

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
    /// Returns the list of chat-capable model IDs available on the Gemini API.
    /// Only models that support <c>generateContent</c> are included.
    /// </summary>
    /// <param name="config">Provider configuration. Must be a <see cref="GeminiProtocolConfig"/> instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of model ID strings, or an error.</returns>
    public override async Task<Result<IReadOnlyList<string>, LlmError>> GetAvailableModelsAsync(
        ModelConfig config,
        CancellationToken ct)
    {
        if (!TryGetConfig(config, out GeminiProtocolConfig geminiConfig, out LlmError configError))
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(configError);
        }

        string url = $"{geminiConfig.BaseUrl}/models?key={geminiConfig.ApiKey}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var bodyResult = await SendForStringAsync(request, ct);
        if (bodyResult is Result<string, LlmError>.Err bodyErr)
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(bodyErr.Error);
        }

        var json = ((Result<string, LlmError>.Ok)bodyResult).Value;
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

        return cfg;
    }

    // HELPERS =====================================================================================

    private async Task<Result<HttpResponseMessage, LlmError>> SendHttpRequestAsync(
        Conversation conversation,
        string systemPrompt,
        GeminiProtocolConfig config,
        CancellationToken ct)
    {
        var body = BuildRequestBody(conversation, systemPrompt, config);
        var json = body.ToJsonString();

        // API key is passed as a query parameter; alt=sse requests SSE-formatted streaming.
        string url = $"{config.BaseUrl}/models/{config.ModelId}:streamGenerateContent?key={config.ApiKey}&alt=sse";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return await SendStreamingRequestAsync(request, ct);
    }

    private JsonObject BuildRequestBody(
        Conversation conversation,
        string systemPrompt,
        GeminiProtocolConfig config)
    {
        var body = new JsonObject
        {
            ["contents"] = BuildContents(conversation),
            ["generationConfig"] = BuildGenerationConfig(config),
        };

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = systemPrompt },
                },
            };
        }

        return body;
    }

    private static async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> ParseSseStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

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

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                string? contentDelta = null;
                bool isLast = false;
                LlmUsage? usage = null;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];

                    // Text delta — concatenate all text parts in this chunk.
                    if (candidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textEl) &&
                                textEl.ValueKind == JsonValueKind.String)
                            {
                                sb.Append(textEl.GetString());
                            }
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
                        new LlmResponseChunk(contentDelta, isLast, usage));
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

    private static JsonArray BuildContents(Conversation conversation)
    {
        var contents = new JsonArray();

        foreach (var message in conversation.Messages)
        {
            // Gemini uses "model" for the assistant role, not "assistant".
            string role = message.Role == Role.User ? "user" : "model";

            var parts = new JsonArray();
            foreach (var block in message.Content)
            {
                parts.Add(BuildPart(block));
            }

            contents.Add(new JsonObject { ["role"] = role, ["parts"] = parts });
        }

        return contents;
    }

    private static JsonNode BuildPart(MessageContent block)
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
                    ["args"] = JsonNode.Parse(call.InputJson) ?? new JsonObject(),
                },
            },

            ToolResultContent result => new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = result.ToolCallId,
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
