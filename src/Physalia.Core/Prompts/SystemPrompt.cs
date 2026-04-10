// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Prompts;

/// <summary>
/// Provides system prompt components for LLM requests.
/// </summary>
public static class SystemPrompt
{
    /// <summary>
    /// The shared preamble sent on every request, establishing the GH/Rhino expert role
    /// and geometry constraints common to all Zooid types.
    /// </summary>
    public const string Preamble = """
        You are an expert Grasshopper / Rhino developer working in Rhino 8+.
        """;

    /// <summary>
    /// Builds a complete system prompt by combining the shared preamble with a Zooid-specific
    /// format prompt describing the target language and expected response schema.
    /// </summary>
    /// <param name="formatPrompt">The Zooid-specific format and schema instructions.</param>
    /// <returns>The full system prompt string.</returns>
    public static string Build(string formatPrompt) => Preamble + "\n\n" + formatPrompt;
}
