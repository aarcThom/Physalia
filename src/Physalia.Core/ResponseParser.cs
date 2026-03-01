using System.Text.Json;
using System.Text.RegularExpressions;
using Physalia.Core.Models;

namespace Physalia.Core.Parsing;

public static class ResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public static ScriptResponse Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            throw new Exception("Reponse is empty.");

        // LLMS sometimes wrap JSON in ```json...``` markdown fences.
        string json = StripCodeFences(rawText).Trim();

        var result = JsonSerializer.Deserialize<ScriptResponse>(json, JsonOptions);

        if (result is null)
            throw new Exception("JSON deserialization returned null.");

        return result;
    }


    private static string StripCodeFences(string text)
    {
        // Matches ```json ... ``` or ``` ... ``` or atleast Clause says it does...
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```");
        return match.Success ? match.Groups[1].Value : text;
    }
}