// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Physalia.Core.Parsing;

/// <summary>
/// Parses raw LLM response text into a <see cref="ScriptResponse"/>,
/// stripping any accidental markdown code fences before deserializing.
/// </summary>
public static class ResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new ()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Strips markdown code fences from the raw response and deserializes the JSON into a <see cref="ScriptResponse"/>.
    /// </summary>
    /// <param name="rawText">The raw text returned by the LLM, which may contain markdown fences.</param>
    /// <returns>A populated <see cref="ScriptResponse"/> instance.</returns>
    /// <exception cref="Exception">Thrown when the raw text is empty or deserialization returns null.</exception>
    public static ScriptResponse Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new Exception("Reponse is empty.");
        }

        // LLMS sometimes wrap JSON in ```json...``` markdown fences.
        string json = StripCodeFences(rawText).Trim();

        var result = JsonSerializer.Deserialize<ScriptResponse>(json, JsonOptions) ?? throw new Exception("JSON deserialization returned null.");
        return result;
    }

    private static string StripCodeFences(string text)
    {
        // Matches ```json ... ``` or ``` ... ``` or atleast Clause says it does...
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```");
        return match.Success ? match.Groups[1].Value : text;
    }
}
