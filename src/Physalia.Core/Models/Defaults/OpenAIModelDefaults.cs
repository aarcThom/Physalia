// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Models.Defaults;

/// <summary>
/// The living table of known model behaviours on the OpenAI Chat Completions wire format
/// (OpenAI, DeepSeek, OpenRouter, Groq, llama.cpp). The request builder consults this to
/// shape per-model parameters so models work correctly with nothing wired into a Tweaker;
/// explicit Tweaker values still override. Update the table as providers ship models —
/// this file is the single place that knowledge lives.
/// </summary>
/// <remarks>
/// Design guidelines for editing this table: <c>planning/model-defaults.md</c> (repo root).
/// Sources (checked 2026-07-11): api-docs.deepseek.com/guides/thinking_mode (DeepSeek V4
/// emits reasoning_content only when the body carries <c>thinking: { type: "enabled" }</c>;
/// deepseek-chat/deepseek-reasoner retire 2026-07-24) and the OpenAI reasoning-model docs
/// (o-series / GPT-5 require <c>max_completion_tokens</c> instead of <c>max_tokens</c> and
/// reject sampling parameters).
/// </remarks>
public static class OpenAIModelDefaults
{
    /// <summary>
    /// The behavioural profile of a model family on the OpenAI protocol.
    /// </summary>
    /// <param name="ThinkingOnByDefault">
    /// Whether to send <c>thinking: { type: "enabled" }</c> when the config does not say
    /// otherwise — the opt-in DeepSeek V4 requires before it emits reasoning.
    /// </param>
    /// <param name="UsesMaxCompletionTokens">
    /// Whether the model requires <c>max_completion_tokens</c> instead of the deprecated
    /// <c>max_tokens</c> (OpenAI reasoning models reject the latter).
    /// </param>
    /// <param name="AllowsSampling">
    /// Whether temperature/top_p are accepted. OpenAI reasoning models reject them.
    /// </param>
    public sealed record Entry(
        bool ThinkingOnByDefault,
        bool UsesMaxCompletionTokens,
        bool AllowsSampling);

    /// <summary>
    /// Profile for models not in the table: plain chat-completions behaviour.
    /// </summary>
    public static Entry Fallback { get; } = new(
        ThinkingOnByDefault: false,
        UsesMaxCompletionTokens: false,
        AllowsSampling: true);

    private static readonly Entry OpenAIReasoning = new(false, true, false);
    private static readonly Entry DeepSeekThinking = new(true, false, true);

    // Matched with Contains against the model ID with any "provider/" namespace stripped.
    // Ordered — first match wins.
    private static readonly (string Pattern, Entry Entry)[] ContainsModels =
    {
        // DeepSeek V4: thinking must be requested explicitly to see reasoning_content.
        // (deepseek-reasoner thinks without the field, so it is intentionally absent here.)
        ("deepseek-v4", DeepSeekThinking),
    };

    // Matched with StartsWith against the namespace-stripped, lowercased model ID —
    // "o1"/"o3"/"o4" as Contains patterns would false-match unrelated names.
    private static readonly (string Prefix, Entry Entry)[] PrefixModels =
    {
        ("gpt-5", OpenAIReasoning),
        ("o1", OpenAIReasoning),
        ("o3", OpenAIReasoning),
        ("o4", OpenAIReasoning),
    };

    /// <summary>
    /// Resolves the behavioural profile for a model ID, falling back to
    /// <see cref="Fallback"/> for unknown models. OpenRouter-style namespaces
    /// ("openai/o3-mini") are stripped before matching.
    /// </summary>
    /// <param name="modelId">The model identifier.</param>
    /// <returns>The matching profile, or the fallback.</returns>
    public static Entry Resolve(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return Fallback;
        }

        int slash = modelId.LastIndexOf('/');
        string normalized = (slash >= 0 ? modelId[(slash + 1) ..] : modelId).Trim();

        foreach ((string pattern, Entry entry) in ContainsModels)
        {
            if (normalized.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        foreach ((string prefix, Entry entry) in PrefixModels)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return Fallback;
    }
}
