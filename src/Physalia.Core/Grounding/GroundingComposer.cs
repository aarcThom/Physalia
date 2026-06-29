// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;

namespace Physalia.Core.Grounding;

/// <summary>
/// Pure assembler that folds grounding sections into a system prompt. Keeps the Composer
/// component thin and the assembly logic testable without a Grasshopper dependency.
/// </summary>
public static class GroundingComposer
{
    /// <summary>
    /// Appends each grounding's system-prompt section to the prompt, separated by blank lines.
    /// Empty or whitespace-only sections are dropped.
    /// </summary>
    /// <param name="systemPrompt">The already-assembled system prompt.</param>
    /// <param name="groundings">The groundings to fold in.</param>
    /// <returns>
    /// The system prompt with each non-empty grounding section appended. Returns the prompt
    /// unchanged when there are no groundings (or none contribute text).
    /// </returns>
    public static string Append(string systemPrompt, IReadOnlyList<Grounding> groundings)
    {
        if (groundings is null || groundings.Count == 0)
        {
            return systemPrompt ?? string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            parts.Add(systemPrompt);
        }

        foreach (Grounding grounding in groundings)
        {
            string section = grounding?.ToSystemPromptSection() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(section))
            {
                parts.Add(section.Trim());
            }
        }

        return string.Join("\n\n", parts);
    }
}
