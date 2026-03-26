// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace Physalia.Core.Config;

/// <summary>
/// Reads API keys from a JSON file keyed by provider name.
/// </summary>
public class ApiKeyResolver
{
    private readonly string _keysFilePath = KeyFilePath;

    /// <summary>
    /// Gets the absolute path to the API keys JSON file, located next to the assembly.
    /// </summary>
    public static string KeyFilePath { get; } = GetApiKeyFile();

    /// <summary>
    /// Reads the API key for the given provider name.
    /// </summary>
    /// <param name="providerName">The provider name to look up, e.g. "anthropic".</param>
    /// <returns>The API key string for the specified provider.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the keys file does not exist at the configured path.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no entry exists for the given provider name.</exception>
    /// <exception cref="Exception">Thrown when the file cannot be parsed or the key is empty.</exception>
    public string GetKey(string providerName)
    {
        if (!File.Exists(_keysFilePath))
        {
            throw new FileNotFoundException($"Keys file not found at: {_keysFilePath}");
        }

        var json = File.ReadAllText(_keysFilePath);
        var keys = JsonSerializer.Deserialize<Dictionary<string, ProviderEntry>>(json) ?? throw new Exception($"Failed to parse {_keysFilePath}");

        if (!keys.TryGetValue(providerName, out var entry))
        {
            throw new KeyNotFoundException($"No entry for '{providerName}' in {_keysFilePath}");
        }

        if (string.IsNullOrWhiteSpace(entry.ApiKey))
        {
            throw new Exception($"API key for '{providerName}' is empty in {_keysFilePath}");
        }

        return entry.ApiKey;
    }

    /// <summary>
    /// Returns all provider names that have a non-placeholder API key configured in the keys file.
    /// Used to populate the provider dropdown in the providerSelector component.
    /// </summary>
    /// <returns>A list of valid provider names, or an empty list when the file cannot be read.</returns>
    public List<string> GetAvailableProviders()
    {
        if (!File.Exists(_keysFilePath))
        {
            return new List<string>();
        }

        var json = File.ReadAllText(_keysFilePath);
        var keys = JsonSerializer.Deserialize<Dictionary<string, ProviderEntry>>(json);

        if (keys is null)
        {
            return new List<string>();
        }

        return keys
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.ApiKey)
                         && !kv.Value.ApiKey.StartsWith("YOUR_"))
            .Select(kv => kv.Key)
            .ToList();
    }

    private static string GetApiKeyFile()
    {
        return Path.Combine(Path.GetDirectoryName(typeof(ApiKeyResolver).Assembly.Location) !, "physaliaKeys.json");
    }

    private class ProviderEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;
    }
}
