// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Physalia.Core.Config.Secrets;

namespace Physalia.Core.Config;

/// <summary>
/// Which providers the user has actually connected to Physalia.
/// </summary>
/// <remarks>
/// <para><b>Availability is not consent.</b> A <c>GEMINI_API_KEY</c> in the environment, or a Claude
/// Code CLI on PATH, means a provider *could* be used — not that the user wants this plug-in
/// spending their quota through it. Both were previously treated as configuration, so a machine with
/// unrelated tooling installed silently arrived pre-wired to providers nobody had chosen. This file
/// is the opt-in, and a provider is only offered to the pipeline once it appears here.</para>
/// <para><b>Deliberately NOT in the encrypted store.</b> It holds no secrets — provider ids and a
/// timestamp — and keeping it plain has two concrete payoffs: the user can read and edit it, and it
/// survives a credential store that cannot be decrypted (a rebuilt Windows profile), so the window
/// can still say *which* providers were connected while asking for their keys again.</para>
/// </remarks>
public sealed class ProviderActivation
{
    /// <summary>
    /// The file this is stored in, inside Physalia's per-user data folder.
    /// </summary>
    public const string FileName = "providers.json";

    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();

    private Dictionary<string, Entry>? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderActivation"/> class.
    /// </summary>
    /// <param name="path">Absolute path of the JSON file backing this list.</param>
    public ProviderActivation(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path is required.", nameof(path));

        this._path = path;
    }

    /// <summary>
    /// Gets an activation list in Physalia's per-user data folder, beside the credential store.
    /// </summary>
    /// <returns>The activation list.</returns>
    public static ProviderActivation Default() =>
        new(Path.Combine(SecretStores.DataFolder(), FileName));

    /// <summary>
    /// Returns whether the user has connected this provider.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    /// <returns>True when the provider has been opted into.</returns>
    public bool IsActivated(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return false;

        lock (this._gate)
        {
            this.Load();
            return this._cache!.TryGetValue(providerId, out Entry? entry) && entry.Enabled;
        }
    }

    /// <summary>
    /// Records that the user has connected this provider.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    public void Activate(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return;

        lock (this._gate)
        {
            this.Load();
            this._cache![providerId] = new Entry
            {
                Enabled = true,
                ConnectedUtc = DateTime.UtcNow.ToString("O"),
            };
            this.Persist();
        }
    }

    /// <summary>
    /// Removes a provider from the connected list.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    public void Deactivate(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return;

        lock (this._gate)
        {
            this.Load();
            if (this._cache!.Remove(providerId))
                this.Persist();
        }
    }

    /// <summary>
    /// Returns every connected provider id.
    /// </summary>
    /// <returns>The activated ids, in the order they are stored.</returns>
    public IReadOnlyList<string> ActivatedIds()
    {
        lock (this._gate)
        {
            this.Load();
            return this._cache!.Where(kv => kv.Value.Enabled).Select(kv => kv.Key).ToList();
        }
    }

    /// <summary>
    /// Drops the cached read so the next access goes back to the file.
    /// </summary>
    public void Invalidate()
    {
        lock (this._gate)
        {
            this._cache = null;
        }
    }

    // Never throws. An unreadable or corrupt list means "nothing connected yet", which is a state the
    // UI already renders — the setup screen — rather than a failure anyone can act on.
    private void Load()
    {
        if (this._cache is not null)
            return;

        this._cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(this._path))
                return;

            string text = File.ReadAllText(this._path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
                return;

            Document? document = JsonSerializer.Deserialize<Document>(text, SerializerOptions);
            if (document?.Providers is null)
                return;

            foreach (KeyValuePair<string, Entry> pair in document.Providers)
                this._cache[pair.Key] = pair.Value;
        }
        catch (Exception)
        {
            this._cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Persist()
    {
        try
        {
            string? dir = Path.GetDirectoryName(this._path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var document = new Document { Version = CurrentVersion, Providers = this._cache };
            File.WriteAllText(this._path, JsonSerializer.Serialize(document, SerializerOptions), Encoding.UTF8);
        }
        catch (Exception)
        {
            // The in-memory list still serves this session; a read-only disk costs the user a
            // re-connect next launch rather than the session they are in.
        }
    }

    private sealed class Document
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("providers")]
        public Dictionary<string, Entry>? Providers { get; set; }
    }

    private sealed class Entry
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("connectedUtc")]
        public string? ConnectedUtc { get; set; }
    }
}
