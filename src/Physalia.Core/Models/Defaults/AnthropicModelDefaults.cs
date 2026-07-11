// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Models.Defaults;

/// <summary>
/// The living table of known Anthropic model behaviours. The request builder consults
/// this to shape thinking and sampling parameters per model, so models work correctly
/// with nothing wired into a Tweaker; explicit Tweaker values still override.
/// Update the table as Anthropic ships models — this file is the single place that
/// knowledge lives.
/// </summary>
/// <remarks>
/// Design guidelines for editing this table: <c>planning/model-defaults.md</c> (repo root).
/// Source: platform.claude.com/docs/en/build-with-claude/adaptive-thinking
/// (checked 2026-07-11). Generational summary: Sonnet 4.6 / Opus 4.6 and earlier accept
/// the manual <c>enabled</c>/<c>budget_tokens</c> form and normal sampling parameters.
/// Sonnet 5 / Opus 4.7+ / Fable / Mythos are adaptive-only (manual form is 400-rejected),
/// reject non-default temperature/top_p/top_k on every request, and default their thinking
/// display to "omitted" (empty thinking deltas) — visible thinking must be requested via
/// <c>display: "summarized"</c>.
/// </remarks>
public static class AnthropicModelDefaults
{
    /// <summary>
    /// The behavioural profile of an Anthropic model family.
    /// </summary>
    /// <param name="SupportsAdaptive">Whether <c>thinking: { type: "adaptive" }</c> is accepted.</param>
    /// <param name="SupportsManualBudget">Whether <c>thinking: { type: "enabled", budget_tokens }</c> is accepted.</param>
    /// <param name="ThinkingOnByDefault">
    /// Whether the API runs thinking (and bills for it) even when the request carries no
    /// thinking config. When true, the request builder asks for summarized display by
    /// default so the billed thinking is actually visible.
    /// </param>
    /// <param name="SupportsDisabled">Whether <c>thinking: { type: "disabled" }</c> is accepted.</param>
    /// <param name="AllowsSampling">
    /// Whether non-default temperature/top_p/top_k are accepted. The newest generations
    /// reject them on every request, thinking or not.
    /// </param>
    public sealed record Entry(
        bool SupportsAdaptive,
        bool SupportsManualBudget,
        bool ThinkingOnByDefault,
        bool SupportsDisabled,
        bool AllowsSampling);

    /// <summary>
    /// Conservative profile for models not in the table: manual budget form only,
    /// thinking off unless requested, sampling parameters accepted. Matches Sonnet 4.5 /
    /// Opus 4.5 and earlier thinking-capable models.
    /// </summary>
    public static Entry Fallback { get; } = new(
        SupportsAdaptive: false,
        SupportsManualBudget: true,
        ThinkingOnByDefault: false,
        SupportsDisabled: true,
        AllowsSampling: true);

    // Ordered — first substring match wins, so put more specific patterns first.
    // Patterns are matched case-insensitively against the full model ID, which keeps
    // date-suffixed IDs (claude-sonnet-5-20260201) and namespaced IDs working.
    private static readonly (string Pattern, Entry Entry)[] KnownModels =
    {
        // Fable / Mythos: adaptive always on, cannot be disabled, no sampling params.
        ("fable", new Entry(true, false, true, false, false)),
        ("mythos-preview", new Entry(true, true, true, false, false)),
        ("mythos", new Entry(true, false, true, false, false)),

        // Opus 4.7 / 4.8: adaptive-only, thinking off unless requested, no sampling params.
        ("opus-4-8", new Entry(true, false, false, true, false)),
        ("opus-4-7", new Entry(true, false, false, true, false)),

        // Sonnet 5: adaptive-only, thinking ON by default, no sampling params.
        ("sonnet-5", new Entry(true, false, true, true, false)),

        // Opus 4.6 / Sonnet 4.6: adaptive and (deprecated) manual both accepted,
        // thinking off by default, sampling params accepted.
        ("opus-4-6", new Entry(true, true, false, true, true)),
        ("sonnet-4-6", new Entry(true, true, false, true, true)),
    };

    /// <summary>
    /// Resolves the behavioural profile for a model ID, falling back to
    /// <see cref="Fallback"/> for unknown models.
    /// </summary>
    /// <param name="modelId">The Anthropic model identifier.</param>
    /// <returns>The matching profile, or the conservative fallback.</returns>
    public static Entry Resolve(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return Fallback;
        }

        foreach ((string pattern, Entry entry) in KnownModels)
        {
            if (modelId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return Fallback;
    }
}
