// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Models.Defaults;

/// <summary>
/// The living table of known Gemini model behaviours. The request builder consults this
/// so thinking-capable Gemini models return visible thought summaries with nothing wired
/// into a Tweaker; explicit Tweaker values still override. Update the table as Google
/// ships models — this file is the single place that knowledge lives.
/// </summary>
/// <remarks>
/// Design guidelines for editing this table: <c>planning/model-defaults.md</c> (repo root).
/// Gemini 2.5+ models think by default but only return thought summaries when the request
/// carries <c>thinkingConfig.includeThoughts: true</c>. Older models (2.0 and earlier)
/// reject <c>thinkingConfig</c> entirely, so it must stay omitted for them.
/// </remarks>
public static class GeminiModelDefaults
{
    /// <summary>
    /// The behavioural profile of a Gemini model family.
    /// </summary>
    /// <param name="IncludeThoughtsByDefault">
    /// Whether to send <c>thinkingConfig: { includeThoughts: true }</c> when the config
    /// does not specify a thinking budget, making default thinking visible.
    /// </param>
    public sealed record Entry(bool IncludeThoughtsByDefault);

    /// <summary>
    /// Profile for models not in the table: no thinkingConfig is sent.
    /// </summary>
    public static Entry Fallback { get; } = new(IncludeThoughtsByDefault: false);

    // Ordered — first substring match wins (case-insensitive).
    private static readonly (string Pattern, Entry Entry)[] KnownModels =
    {
        ("gemini-3", new Entry(true)),
        ("gemini-2.5", new Entry(true)),
    };

    /// <summary>
    /// Resolves the behavioural profile for a model ID, falling back to
    /// <see cref="Fallback"/> for unknown models.
    /// </summary>
    /// <param name="modelId">The Gemini model identifier.</param>
    /// <returns>The matching profile, or the fallback.</returns>
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
