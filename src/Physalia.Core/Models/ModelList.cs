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
/// Provides access to LLM model metadata, merged from two public, no-auth catalogs:
/// the OpenRouter model list (primary — ~400+ models across every major provider, kept current)
/// and the LiteLLM model database (gap-filler). Call <see cref="FetchAsync"/> once at startup to
/// obtain the hydrated dictionary; all query methods are pure functions over that dictionary.
///
/// <para>Both catalogs are keyed under several normalised variants of each model id (provider
/// prefix stripped, date/version suffix stripped, dotted/dashed version forms) so a lookup hits
/// regardless of which id form the user's provider uses — e.g. Anthropic's dated
/// <c>claude-sonnet-4-5-20250929</c> resolves to OpenRouter's <c>anthropic/claude-sonnet-4.5</c>.</para>
/// </summary>
public static class ModelList
{
    private const string OpenRouterUrl = "https://openrouter.ai/api/v1/models";

    private const string LiteLlmUrl =
        "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";

    /// <summary>
    /// Fetches and merges the OpenRouter and LiteLLM catalogs into one dictionary keyed by model id
    /// (and normalised variants). OpenRouter is parsed first so its richer, more current data wins on
    /// conflicts; LiteLLM fills any gaps. The fetch is resilient: if one source is unreachable the
    /// other is still used, and only a total failure (neither catalog loaded) returns an error.
    /// Pass the caller's shared <see cref="HttpClient"/> — do not instantiate one per call.
    /// </summary>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only dictionary of model entries, or an error when no catalog could be loaded.</returns>
    public static async Task<Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>> FetchAsync(
        HttpClient client,
        CancellationToken ct = default)
    {
        var merged = new Dictionary<string, ModelEntry>(StringComparer.OrdinalIgnoreCase);
        LlmError? lastError = null;

        // OpenRouter first (richer + current) so it wins; LiteLLM then fills gaps via TryAdd.
        foreach (var (url, parse) in new (string, Action<string, Dictionary<string, ModelEntry>>)[]
        {
            (OpenRouterUrl, ParseOpenRouterInto),
            (LiteLlmUrl, ParseLiteLlmInto),
        })
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = new LlmError(HttpErrorMapper.MapStatusCode(response.StatusCode), $"{url}: {(int)response.StatusCode}");
                    continue;
                }

                string json = await response.Content.ReadAsStringAsync(ct);
                parse(json, merged);
            }
            catch (OperationCanceledException)
            {
                return new Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Err(
                    new LlmError(LlmErrorKind.Cancelled, "Request was cancelled."));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                lastError = new LlmError(LlmErrorKind.Network, $"{url}: {ex.Message}");
            }
        }

        if (merged.Count == 0)
        {
            return new Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Err(
                lastError ?? new LlmError(LlmErrorKind.Network, "No model catalog could be loaded."));
        }

        return new Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Ok(
            new ReadOnlyDictionary<string, ModelEntry>(merged));
    }

    /// <summary>
    /// Looks up a model entry by id, trying every normalised lookup-key variant (exact, provider
    /// prefix stripped, last path segment, date/version-canonicalised) and finally the GGUF form.
    /// </summary>
    /// <param name="models">The hydrated model dictionary.</param>
    /// <param name="modelId">The model id to look up.</param>
    /// <returns>The matching entry, or null if not found.</returns>
    public static ModelEntry? Find(
        IReadOnlyDictionary<string, ModelEntry> models,
        string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        foreach (string key in LookupKeys(modelId))
        {
            if (models.TryGetValue(key, out ModelEntry? entry))
            {
                return entry;
            }
        }

        string gguf = NormalizeGgufId(modelId);
        if (gguf != modelId)
        {
            foreach (string key in LookupKeys(gguf))
            {
                if (models.TryGetValue(key, out ModelEntry? entry))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns all distinct entries belonging to a given provider (case-insensitive).
    /// </summary>
    /// <param name="models">The hydrated model dictionary.</param>
    /// <param name="provider">The provider name, e.g. "openai" or "anthropic".</param>
    /// <returns>All distinct model entries for the specified provider.</returns>
    public static IEnumerable<ModelEntry> ByProvider(
        IReadOnlyDictionary<string, ModelEntry> models,
        string provider)
        => models.Values
            .Where(e => string.Equals(e.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Distinct();

    /// <summary>
    /// Strips a leading provider prefix from a namespaced model id.
    /// "openai/gpt-4o" becomes "gpt-4o". IDs without a slash are returned unchanged.
    /// </summary>
    /// <param name="modelId">The model id to normalise.</param>
    /// <returns>The model id with any leading provider prefix removed.</returns>
    public static string NormalizeId(string modelId)
    {
        int slash = modelId.IndexOf('/', StringComparison.Ordinal);
        return slash >= 0 ? modelId[(slash + 1)..] : modelId;
    }

    /// <summary>
    /// Normalises a GGUF filename into a canonical model id suitable for database lookup.
    /// Strips the <c>.gguf</c> extension, quantisation suffix (e.g. Q4_K_M, IQ4_XS, f16),
    /// organisation prefix (e.g. <c>Qwen_Qwen3-14B</c> → <c>Qwen3-14B</c>), then lowercases.
    /// Returns the original string unchanged if it does not look like a GGUF filename.
    /// </summary>
    /// <param name="modelId">The model id or GGUF filename to normalise.</param>
    /// <returns>A lowercase canonical model id, or the original string if no transformation applied.</returns>
    public static string NormalizeGgufId(string modelId)
    {
        if (!modelId.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return modelId;

        // Strip .gguf extension.
        string name = modelId[..^5];

        // Strip quantisation suffix: -Q4_K_M, .Q4_K_M, -IQ4_XS, -f16, -BF16, etc.
        name = s_quantSuffix.Replace(name, string.Empty);

        // Strip org prefix: "Qwen_Qwen3-14B" → "Qwen3-14B"
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

    // KEY NORMALISATION ===========================================================================

    /// <summary>
    /// Produces the ordered set of lookup keys for a model id, most specific first: the lowercased
    /// id, its provider-prefix-stripped and last-segment forms, and a date/version-canonicalised
    /// variant of each. Used to both index the catalogs and resolve a query, so the two always meet.
    /// </summary>
    /// <param name="id">The model id.</param>
    /// <returns>Distinct candidate lookup keys, most specific first.</returns>
    private static IEnumerable<string> LookupKeys(string id)
    {
        var keys = new List<string>();

        void Add(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return;
            }

            string lower = s.ToLowerInvariant();
            keys.Add(lower);

            string canonical = Canonicalize(lower);
            if (canonical != lower)
            {
                keys.Add(canonical);
            }
        }

        Add(id);

        int firstSlash = id.IndexOf('/', StringComparison.Ordinal);
        if (firstSlash >= 0)
        {
            Add(id[(firstSlash + 1)..]);
        }

        int lastSlash = id.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash != firstSlash)
        {
            Add(id[(lastSlash + 1)..]);
        }

        return keys.Distinct();
    }

    /// <summary>
    /// Canonicalises an id for fuzzy matching across providers: strips a trailing variant tag
    /// (<c>:free</c>, <c>:nitro</c>, …), a trailing date stamp (<c>-20250929</c> or
    /// <c>-2025-09-29</c>), and converts hyphen-separated version digits to the dotted form
    /// (<c>claude-sonnet-4-5</c> → <c>claude-sonnet-4.5</c>, matching OpenRouter's slugs).
    /// </summary>
    /// <param name="lowerId">An already-lowercased id.</param>
    /// <returns>The canonicalised id.</returns>
    private static string Canonicalize(string lowerId)
    {
        int colon = lowerId.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            lowerId = lowerId[..colon];
        }

        lowerId = s_dateSuffix.Replace(lowerId, string.Empty);
        lowerId = s_versionDash.Replace(lowerId, "$1.$2");
        return lowerId;
    }

    // HELPERS =====================================================================================

    private static readonly Regex s_quantSuffix = new Regex(
        @"[-_.](?:(?:IQ|Q)\d+[A-Z0-9_]*|[Ff](?:16|32)|BF16|GGUF)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex s_dateSuffix = new Regex(
        @"[-@](?:\d{8}|\d{4}-\d{2}-\d{2})$",
        RegexOptions.Compiled);

    private static readonly Regex s_versionDash = new Regex(
        @"(\d)-(\d)",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses the OpenRouter <c>/api/v1/models</c> response and adds each model under all of its
    /// lookup-key variants (first writer wins per key, so a catalog merged earlier keeps priority).
    /// </summary>
    private static void ParseOpenRouterInto(string json, Dictionary<string, ModelEntry> dict)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement el in data.EnumerateArray())
        {
            string id = el.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            if (id.Length == 0)
            {
                continue;
            }

            int maxInput = el.TryGetProperty("context_length", out JsonElement ctxEl) ? ReadInt(ctxEl) : 0;
            if (maxInput <= 0)
            {
                continue;
            }

            int maxOutput = 0;
            if (el.TryGetProperty("top_provider", out JsonElement top)
                && top.TryGetProperty("max_completion_tokens", out JsonElement maxOutEl))
            {
                maxOutput = ReadInt(maxOutEl);
            }

            bool vision = false;
            if (el.TryGetProperty("architecture", out JsonElement arch)
                && arch.TryGetProperty("input_modalities", out JsonElement modalities)
                && modalities.ValueKind == JsonValueKind.Array)
            {
                vision = modalities.EnumerateArray().Any(m => string.Equals(m.GetString(), "image", StringComparison.OrdinalIgnoreCase));
            }

            bool tools = false;
            bool reasoning = false;
            if (el.TryGetProperty("supported_parameters", out JsonElement supported) && supported.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement p in supported.EnumerateArray())
                {
                    string? name = p.GetString();
                    if (name is "tools" or "tool_choice")
                    {
                        tools = true;
                    }
                    else if (name is "reasoning" or "include_reasoning")
                    {
                        reasoning = true;
                    }
                }
            }

            int prefix = id.IndexOf('/', StringComparison.Ordinal);
            string provider = prefix > 0 ? id[..prefix] : string.Empty;

            var entry = new ModelEntry(provider, id, maxInput, maxOutput, tools, vision, reasoning);
            AddWithVariants(dict, id, entry);

            // Also index the canonical (dated) slug if OpenRouter supplied one.
            if (el.TryGetProperty("canonical_slug", out JsonElement slugEl) && slugEl.GetString() is { Length: > 0 } slug)
            {
                AddWithVariants(dict, slug, entry);
            }
        }
    }

    /// <summary>
    /// Parses the LiteLLM <c>model_prices_and_context_window.json</c> object and adds each chat
    /// model (with a valid context window) under all of its lookup-key variants.
    /// </summary>
    private static void ParseLiteLlmInto(string json, Dictionary<string, ModelEntry> dict)
    {
        using JsonDocument doc = JsonDocument.Parse(json);

        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            JsonElement el = prop.Value;
            if (el.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Skip non-chat modes (embeddings, image generation, audio, reranking, etc.).
            if (el.TryGetProperty("mode", out JsonElement modeEl))
            {
                string? mode = modeEl.GetString();
                if (mode != null && mode != "chat")
                {
                    continue;
                }
            }

            if (!el.TryGetProperty("max_input_tokens", out JsonElement maxInputEl))
            {
                continue;
            }

            int maxInput = ReadInt(maxInputEl);
            if (maxInput <= 0)
            {
                continue;
            }

            int maxOutput = el.TryGetProperty("max_output_tokens", out JsonElement maxOutputEl) ? ReadInt(maxOutputEl) : 0;

            string provider = el.TryGetProperty("litellm_provider", out JsonElement provEl) ? provEl.GetString() ?? string.Empty : string.Empty;
            bool toolCalls = el.TryGetProperty("supports_function_calling", out JsonElement tcEl) && tcEl.ValueKind == JsonValueKind.True;
            bool vision = el.TryGetProperty("supports_vision", out JsonElement visEl) && visEl.ValueKind == JsonValueKind.True;
            bool reasoning = el.TryGetProperty("supports_reasoning", out JsonElement reasonEl) && reasonEl.ValueKind == JsonValueKind.True;

            var entry = new ModelEntry(provider, prop.Name, maxInput, maxOutput, toolCalls, vision, reasoning);
            AddWithVariants(dict, prop.Name, entry);
        }
    }

    private static void AddWithVariants(Dictionary<string, ModelEntry> dict, string id, ModelEntry entry)
    {
        foreach (string key in LookupKeys(id))
        {
            dict.TryAdd(key, entry);
        }
    }

    private static int ReadInt(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number)
        {
            if (el.TryGetInt32(out int i))
            {
                return i;
            }

            if (el.TryGetInt64(out long l))
            {
                return l > int.MaxValue ? int.MaxValue : (int)l;
            }

            if (el.TryGetDouble(out double d) && d > 0)
            {
                return d > int.MaxValue ? int.MaxValue : (int)d;
            }
        }

        return 0;
    }
}
