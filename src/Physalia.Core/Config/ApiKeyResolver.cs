using System.Text.Json;

namespace Physalia.Core.Config
{
    public class ApiKeyResolver
    {
        private readonly string _keysFilePath;

        public ApiKeyResolver(string keysFilePath)
        {
            _keysFilePath = keysFilePath;
        }

        /// <summary>
        /// Reads the API key for the given provider name (e.g. "Claude Code", "OpenAI").
        /// </summary>
        public string GetKey(string providerName)
        {
            if (!File.Exists(_keysFilePath))
                throw new FileNotFoundException(
                    $"Keys file not found at: {_keysFilePath}");

            var json = File.ReadAllText(_keysFilePath);
            var keys = JsonSerializer.Deserialize<Dictionary<string, ProviderEntry>>(json);

            if (keys is null)
                throw new Exception($"Failed to parse {_keysFilePath}");

            if (!keys.TryGetValue(providerName, out var entry))
                throw new KeyNotFoundException(
                    $"No entry for '{providerName}' in {_keysFilePath}");

            if (string.IsNullOrWhiteSpace(entry.ApiKey))
                throw new Exception(
                    $"API key for '{providerName}' is empty in {_keysFilePath}");

            return entry.ApiKey;
        }

        /// <summary>
        /// Returns all provider names that have a non-empty key.
        /// Used for populating GH dropdown list
        /// </summary>
        public List<string> GetAvailableProviders()
        {
            if (!File.Exists(_keysFilePath))
                return new List<string>();

            var json = File.ReadAllText(_keysFilePath);
            var keys = JsonSerializer.Deserialize<Dictionary<string, ProviderEntry>>(json);

            if (keys is null)
                return new List<string>();

            return keys
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.ApiKey)
                             && !kv.Value.ApiKey.StartsWith("YOUR_"))
                .Select(kv => kv.Key)
                .ToList();
        }

        private class ProviderEntry
        {
            [System.Text.Json.Serialization.JsonPropertyName("api_key")]
            public string ApiKey { get; set; } = string.Empty;
        }
    }
}
