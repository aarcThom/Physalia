// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;

namespace Physalia.Core.Models;

/// <summary>
/// Provides access to LLM model metadata sourced from the LiteLLM model database.
/// Call <see cref="FetchAsync"/> once at startup to obtain the hydrated dictionary.
/// All query methods are pure functions that accept the dictionary as a parameter.
/// </summary>
public static class ModelList
{
    private const string LiteLlmUrl =
        "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";

    /// <summary>
    /// Fetches the LiteLLM model database and returns a flat dictionary keyed by model ID.
    /// Only chat-mode entries with a valid context window are included.
    /// Pass the caller's shared <see cref="HttpClient"/> — do not instantiate one per call.
    /// </summary>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only dictionary of model entries keyed by model ID, or an error.</returns>
    public static async Task<Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>> FetchAsync(
        HttpClient client,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await client.GetAsync(LiteLlmUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return new Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Err(
                    new LlmError(HttpErrorMapper.MapStatusCode(response.StatusCode), body));
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return new Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Ok(ParseEntries(json));
        }
        catch (OperationCanceledException)
        {
            return new Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Request was cancelled."));
        }
        catch (HttpRequestException ex)
        {
            return new Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Err(
                new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    /// <summary>
    /// Looks up a model entry by ID.
    /// Tries in order: exact match, provider-prefix-stripped form, GGUF-normalised form.
    /// </summary>
    /// <param name="models">The hydrated model dictionary.</param>
    /// <param name="modelId">The model ID to look up.</param>
    /// <returns>The matching entry, or null if not found.</returns>
    public static ModelEntry? Find(
        IReadOnlyDictionary<string, ModelEntry> models,
        string modelId)
    {
        if (models.TryGetValue(modelId, out var entry)) return entry;

        var normalized = NormalizeId(modelId);
        if (normalized != modelId && models.TryGetValue(normalized, out entry)) return entry;

        var gguf = NormalizeGgufId(modelId);
        if (gguf != modelId && models.TryGetValue(gguf, out entry)) return entry;

        return null;
    }

    /// <summary>
    /// Returns all entries belonging to a given provider (case-insensitive).
    /// </summary>
    /// <param name="models">The hydrated model dictionary.</param>
    /// <param name="provider">The provider name, e.g. "openai" or "anthropic".</param>
    /// <returns>All model entries for the specified provider.</returns>
    public static IEnumerable<ModelEntry> ByProvider(
        IReadOnlyDictionary<string, ModelEntry> models,
        string provider)
        => models.Values.Where(e =>
            string.Equals(e.Provider, provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Strips a leading provider prefix from a namespaced model ID.
    /// "openai/gpt-4o" becomes "gpt-4o". IDs without a slash are returned unchanged.
    /// </summary>
    /// <param name="modelId">The model ID to normalise.</param>
    /// <returns>The model ID with any leading provider prefix removed.</returns>
    public static string NormalizeId(string modelId)
    {
        int slash = modelId.IndexOf('/', StringComparison.Ordinal);
        return slash >= 0 ? modelId[(slash + 1)..] : modelId;
    }

    /// <summary>
    /// Normalises a GGUF filename into a canonical model ID suitable for database lookup.
    /// Strips the <c>.gguf</c> extension, quantisation suffix (e.g. Q4_K_M, IQ4_XS, f16),
    /// organisation prefix (e.g. <c>Qwen_Qwen3-14B</c> → <c>Qwen3-14B</c>), then lowercases.
    /// Returns the original string unchanged if it does not look like a GGUF filename.
    /// </summary>
    /// <param name="modelId">The model ID or GGUF filename to normalise.</param>
    /// <returns>A lowercase canonical model ID, or the original string if no transformation applied.</returns>
    public static string NormalizeGgufId(string modelId)
    {
        if (!modelId.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return modelId;

        // Strip .gguf extension.
        string name = modelId[..^5];

        // Strip quantisation suffix: -Q4_K_M, .Q4_K_M, -IQ4_XS, -f16, -BF16, etc.
        name = s_quantSuffix.Replace(name, string.Empty);

        // Strip org prefix: "Qwen_Qwen3-14B" → "Qwen3-14B"
        // Rule: if there is a '_', and the segment after it starts with any dash-separated
        // word found in the segment before it, drop the prefix.
        int underscore = name.IndexOf('_', StringComparison.Ordinal);
        if (underscore > 0)
        {
            string orgPart = name[..underscore];
            string modelPart = name[(underscore + 1)..];
            foreach (var word in orgPart.Split('-'))
            {
                if (modelPart.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                {
                    name = modelPart;
                    break;
                }
            }
        }

        return name.Replace('_', '-').ToLowerInvariant();
    }

    // HELPERS =====================================================================================

    private static readonly Regex s_quantSuffix = new Regex(
        @"[-_.](?:(?:IQ|Q)\d+[A-Z0-9_]*|[Ff](?:16|32)|BF16|GGUF)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static IReadOnlyDictionary<string, ModelEntry> ParseEntries(string json)
    {
        var result = new Dictionary<string, ModelEntry>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var el = prop.Value;

            // Skip non-chat modes (embeddings, image generation, audio, reranking, etc.)
            if (el.TryGetProperty("mode", out var modeEl))
            {
                var mode = modeEl.GetString();
                if (mode != null && mode != "chat") continue;
            }

            // Require a valid context window — entries without one aren't useful.
            if (!el.TryGetProperty("max_input_tokens", out var maxInputEl)) continue;
            if (!maxInputEl.TryGetInt32(out int maxInput) || maxInput <= 0) continue;

            int maxOutput = 0;
            if (el.TryGetProperty("max_output_tokens", out var maxOutputEl))
            {
                maxOutputEl.TryGetInt32(out maxOutput);
            }

            string provider = string.Empty;
            if (el.TryGetProperty("litellm_provider", out var provEl))
            {
                provider = provEl.GetString() ?? string.Empty;
            }

            bool toolCalls = el.TryGetProperty("supports_function_calling", out var tcEl)
                && tcEl.ValueKind == JsonValueKind.True;

            bool vision = el.TryGetProperty("supports_vision", out var visEl)
                && visEl.ValueKind == JsonValueKind.True;

            bool reasoning = el.TryGetProperty("supports_reasoning", out var reasonEl)
                && reasonEl.ValueKind == JsonValueKind.True;

            var entry = new ModelEntry(
                Provider: provider,
                ModelId: prop.Name,
                MaxInputTokens: maxInput,
                MaxOutputTokens: maxOutput,
                SupportsToolCalls: toolCalls,
                SupportsVision: vision,
                SupportsReasoning: reasoning);

            result[prop.Name] = entry;

            // Also index under the last path segment (e.g. "openrouter/qwen/qwen3-14b" → "qwen3-14b")
            // so GGUF-normalised lookups can find multi-prefix keys.
            int lastSlash = prop.Name.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                result.TryAdd(prop.Name[(lastSlash + 1)..], entry);
            }
        }

        return new ReadOnlyDictionary<string, ModelEntry>(result);
    }
}
