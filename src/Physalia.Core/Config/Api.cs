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

    // INTERNAL ================================================================================

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
