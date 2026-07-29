// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Grounding;

/// <summary>
/// Pure assembler that folds grounding sections into a system prompt. Keeps the System Prompt
/// component thin and the assembly logic testable without a Grasshopper dependency.
/// </summary>
public static class GroundingComposer
{
    /// <summary>
    /// Appends each grounding's system-prompt section to the prompt as its own segment, tagged
    /// with the grounding's own volatility. Empty or whitespace-only sections are dropped.
    ///
    /// <para>Segments are returned rather than one joined string so a provider can cache the part
    /// that never changes. The caller's wire order does not have to be cache-aware:
    /// <see cref="SystemPrompt"/> sorts every stable segment ahead of every volatile one on
    /// construction, so a canvas-state grounding wired first still lands in the tail.</para>
    /// </summary>
    /// <param name="systemPrompt">The already-assembled base prompt, treated as stable.</param>
    /// <param name="groundings">The groundings to fold in.</param>
    /// <returns>
    /// The segmented system prompt. Returns just the base prompt when there are no groundings
    /// (or none contribute text).
    /// </returns>
    public static SystemPrompt Append(string systemPrompt, IReadOnlyList<Grounding> groundings)
    {
        var segments = new List<SystemPromptSegment>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            segments.Add(new SystemPromptSegment(systemPrompt, SystemPromptStability.Stable));
        }

        foreach (Grounding grounding in groundings ?? Array.Empty<Grounding>())
        {
            string section = grounding?.ToSystemPromptSection() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(section))
            {
                segments.Add(new SystemPromptSegment(
                    section,
                    grounding!.IsVolatile ? SystemPromptStability.Volatile : SystemPromptStability.Stable));
            }
        }

        return new SystemPrompt(segments);
    }
}
