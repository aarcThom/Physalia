// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;

namespace Physalia.Core.Config;

/// <summary>
/// Reads and resolves API keys from <c>API_KEY_CONFIG.YAML</c>.
/// </summary>
/// <remarks>
/// Resolution order per provider:
/// <list type="number">
/// <item>The environment variable named in <c>env_vars</c> (if the variable is set).</item>
/// <item>The direct value in <c>api_keys</c> (if non-empty).</item>
/// </list>
/// Returns an empty list if the file does not exist.
/// The parser expects 2-space YAML indentation as produced by the bundled template.
/// </remarks>
public static class Api
{
    /// <summary>
    /// Reads the YAML config at <paramref name="filePath"/> and returns one
    /// <see cref="ApiKey"/> per provider whose key could be resolved.
    /// </summary>
    /// <param name="filePath">Absolute path to <c>API_KEY_CONFIG.YAML</c>.</param>
    /// <returns>Resolved keys, or an empty list if the file is absent.</returns>
    public static IReadOnlyList<ApiKey> GetKeys(string filePath)
    {
        if (!File.Exists(filePath))
            return Array.Empty<ApiKey>();

        var sections = ParseYaml(File.ReadAllLines(filePath));
        var result = new List<ApiKey>();
        var envResolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sectionName, envVars, apiKeys) in sections)
        {
            // 1. Environment variables — checked first.
            foreach (var (leafKey, envVarName) in envVars)
            {
                if (string.IsNullOrEmpty(envVarName)) continue;

                string provider = leafKey == "api_key" ? sectionName : leafKey;
                string? value = Environment.GetEnvironmentVariable(envVarName);

                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(new ApiKey(provider, value));
                    envResolved.Add(provider);
                }
            }

            // 2. Direct keys — only for providers not already resolved above.
            foreach (var (leafKey, keyValue) in apiKeys)
            {
                if (string.IsNullOrEmpty(keyValue)) continue;

                string provider = leafKey == "api_key" ? sectionName : leafKey;

                if (!envResolved.Contains(provider))
                    result.Add(new ApiKey(provider, keyValue));
            }
        }

        return result;
    }

    /// <summary>
    /// Writes (or updates) a provider API key in the YAML config at <paramref name="filePath"/>,
    /// under <c>{section}.api_keys.{leaf}</c>, preserving the rest of the file (comments, other
    /// providers, env_vars). Creates the file if it does not exist. Used by the chat-window setup
    /// flow when the user pastes a key.
    /// </summary>
    /// <param name="filePath">Absolute path to <c>API_KEY_CONFIG.YAML</c>.</param>
    /// <param name="section">Top-level YAML section (e.g. <c>anthropic</c>, <c>openai_compatible</c>).</param>
    /// <param name="leaf">The <c>api_keys</c> leaf key (e.g. <c>api_key</c>, <c>openai</c>).</param>
    /// <param name="value">The API key value to store.</param>
    public static void SetKey(string filePath, string section, string leaf, string value)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A config file path is required.", nameof(filePath));
        if (string.IsNullOrWhiteSpace(section))
            throw new ArgumentException("A section name is required.", nameof(section));
        if (string.IsNullOrWhiteSpace(leaf))
            throw new ArgumentException("A leaf key is required.", nameof(leaf));

        value ??= string.Empty;

        var lines = File.Exists(filePath)
            ? new List<string>(File.ReadAllLines(filePath))
            : new List<string>();

        if (!TrySetExistingKey(lines, section, leaf, value))
            AppendSection(lines, section, leaf, value);

        File.WriteAllText(filePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    // INTERNAL ================================================================================

    // Replaces (or inserts) the leaf value within an existing section's api_keys subsection.
    // Returns false when the section or its api_keys subsection is absent, so the caller appends.
    private static bool TrySetExistingKey(List<string> lines, string section, string leaf, string value)
    {
        int sectionStart = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (IndentOf(lines[i]) == 0 && KeyOf(lines[i]) == section)
            {
                sectionStart = i;
                break;
            }
        }

        if (sectionStart < 0)
            return false;

        // The section runs until the next top-level key (or end of file).
        int sectionEnd = lines.Count;
        for (int i = sectionStart + 1; i < lines.Count; i++)
        {
            if (IndentOf(lines[i]) == 0 && KeyOf(lines[i]) != null)
            {
                sectionEnd = i;
                break;
            }
        }

        // Locate the api_keys: subsection (indent 2) inside the section.
        int apiKeysLine = -1;
        for (int i = sectionStart + 1; i < sectionEnd; i++)
        {
            if (IndentOf(lines[i]) == 2 && KeyOf(lines[i]) == "api_keys")
            {
                apiKeysLine = i;
                break;
            }
        }

        if (apiKeysLine < 0)
            return false;

        // The api_keys block runs until the next line indented at or below the subsection.
        int blockEnd = sectionEnd;
        for (int i = apiKeysLine + 1; i < sectionEnd; i++)
        {
            string stripped = StripComment(lines[i]);
            if (string.IsNullOrWhiteSpace(stripped))
                continue;
            if (IndentOf(lines[i]) <= 2)
            {
                blockEnd = i;
                break;
            }
        }

        for (int i = apiKeysLine + 1; i < blockEnd; i++)
        {
            if (IndentOf(lines[i]) == 4 && KeyOf(lines[i]) == leaf)
            {
                lines[i] = $"    {leaf}: \"{value}\"";
                return true;
            }
        }

        // Subsection exists but the leaf does not — add it as the first entry.
        lines.Insert(apiKeysLine + 1, $"    {leaf}: \"{value}\"");
        return true;
    }

    // Appends a fresh section with a single api_keys leaf (only when the section was absent).
    private static void AppendSection(List<string> lines, string section, string leaf, string value)
    {
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
            lines.Add(string.Empty);

        lines.Add($"{section}:");
        lines.Add("  api_keys:");
        lines.Add($"    {leaf}: \"{value}\"");
    }

    // Leading-space count of a line (comments ignored), used to read YAML nesting.
    private static int IndentOf(string line)
    {
        string stripped = StripComment(line);
        return stripped.Length - stripped.TrimStart().Length;
    }

    // The key part of a "key: value" line (comment-stripped, trimmed), or null when there is none.
    private static string? KeyOf(string line)
    {
        string stripped = StripComment(line).Trim();
        int colon = stripped.IndexOf(':');
        return colon < 0 ? null : stripped.Substring(0, colon).Trim();
    }

    private static List<(string Name, Dictionary<string, string> EnvVars, Dictionary<string, string> ApiKeys)> ParseYaml(
        string[] lines)
    {
        var sections = new List<(string, Dictionary<string, string>, Dictionary<string, string>)>();

        string currentSection = string.Empty;
        string currentSubsection = string.Empty;
        var currentEnvVars = new Dictionary<string, string>();
        var currentApiKeys = new Dictionary<string, string>();

        foreach (var rawLine in lines)
        {
            string stripped = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(stripped)) continue;

            int indent = stripped.Length - stripped.TrimStart().Length;
            string trimmed = stripped.Trim();

            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;

            string key = trimmed.Substring(0, colonIdx).Trim();
            string value = trimmed.Substring(colonIdx + 1).Trim().Trim('"');

            if (indent == 0)
            {
                // Commit the previous section before starting a new one.
                if (!string.IsNullOrEmpty(currentSection))
                    sections.Add((currentSection, currentEnvVars, currentApiKeys));

                currentSection = key;
                currentSubsection = string.Empty;
                currentEnvVars = new Dictionary<string, string>();
                currentApiKeys = new Dictionary<string, string>();
            }
            else if (indent == 2)
            {
                currentSubsection = key;
            }
            else if (indent == 4)
            {
                if (currentSubsection == "env_vars")
                    currentEnvVars[key] = value;
                else if (currentSubsection == "api_keys")
                    currentApiKeys[key] = value;
            }
        }

        // Commit the final section.
        if (!string.IsNullOrEmpty(currentSection))
            sections.Add((currentSection, currentEnvVars, currentApiKeys));

        return sections;
    }

    /// <summary>
    /// Strips YAML line comments — everything from a <c>#</c> preceded by whitespace onward.
    /// A <c>#</c> embedded inside a value without preceding whitespace is preserved.
    /// </summary>
    private static string StripComment(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
                return line.Substring(0, i);
        }

        return line;
    }
}
