// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Physalia.Core.Config.Secrets;

namespace Physalia.Core.Config;

/// <summary>
/// The set of provider endpoints and credentials the user has configured, kept in one encrypted
/// document.
/// </summary>
/// <remarks>
/// <para>Written only by the chat window's setup page. That is what makes encryption affordable:
/// nobody hand-edits this, so nothing is lost by making it opaque. The plain-text YAML this
/// replaced could never have been encrypted for exactly that reason: being openable in a text editor
/// was the whole point of it.</para>
/// <para><b>Reads are cached.</b> The Model API component re-reads on every solve to keep its
/// Picker's provider list live, and a decrypt per solve per node is real work. The cache holds for
/// <see cref="CacheSeconds"/> and is dropped outright on any write from this process, so the node
/// sees its own save immediately and another Rhino instance's save within a few seconds.</para>
/// </remarks>
public sealed class CredentialStore
{
    /// <summary>
    /// The name the encrypted document is stored under.
    /// </summary>
    public const string StoreName = "credentials";

    private const int CacheSeconds = 3;
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ISecretStore _store;
    private readonly object _gate = new();

    private Dictionary<string, Entry>? _cache;
    private DateTime _cachedAtUtc = DateTime.MinValue;
    private SecretReadStatus _status = SecretReadStatus.Empty;
    private string? _unreadableReason;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialStore"/> class over a given store.
    /// </summary>
    /// <param name="store">Where the encrypted document lives.</param>
    public CredentialStore(ISecretStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this._store = store;
    }

    /// <summary>
    /// Gets a store backed by the platform's own protection, in Physalia's per-user data folder.
    /// </summary>
    /// <returns>A credential store ready to use.</returns>
    public static CredentialStore Default() => new(SecretStores.For(StoreName));

    /// <summary>
    /// Gets a short description of how the underlying store protects its contents.
    /// </summary>
    public string Protection => this._store.Description;

    /// <summary>
    /// Gets a value indicating whether the underlying store encrypts at rest.
    /// </summary>
    public bool IsEncrypted => this._store.IsEncrypted;

    /// <summary>
    /// Gets the reason the store could not be read, or null when it read fine (or is simply empty).
    /// </summary>
    /// <remarks>
    /// Non-null means credentials EXIST but this account cannot decrypt them. Surfacing that
    /// distinctly is the point: reporting "no providers configured" for a store written by another
    /// Windows account sends the user off to re-enter every key they already have.
    /// </remarks>
    public string? UnreadableReason
    {
        get
        {
            lock (this._gate)
            {
                this.Load();
                return this._unreadableReason;
            }
        }
    }

    /// <summary>
    /// Returns every configured provider.
    /// </summary>
    /// <returns>One <see cref="ModelApi"/> per stored entry, in provider-id order.</returns>
    public IReadOnlyList<ModelApi> All()
    {
        lock (this._gate)
        {
            this.Load();
            return this._cache is null
                ? Array.Empty<ModelApi>()
                : this._cache
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new ModelApi(kv.Key, kv.Value.Url ?? string.Empty, kv.Value.Key ?? string.Empty))
                    .ToList();
        }
    }

    /// <summary>
    /// Returns one provider's stored endpoint and credential.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    /// <returns>The stored entry, or null when that provider has none.</returns>
    public ModelApi? Get(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return null;

        lock (this._gate)
        {
            this.Load();

            if (this._cache is null || !this._cache.TryGetValue(providerId, out Entry? entry))
                return null;

            return new ModelApi(providerId, entry.Url ?? string.Empty, entry.Key ?? string.Empty);
        }
    }

    /// <summary>
    /// Stores one provider's endpoint and credential, replacing any previous entry for it.
    /// </summary>
    /// <param name="api">What to store. Its <see cref="ModelApi.Provider"/> is the key.</param>
    public void Save(ModelApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        if (string.IsNullOrWhiteSpace(api.Provider))
            throw new ArgumentException("A provider id is required.", nameof(api));

        lock (this._gate)
        {
            this.Load();

            // A store this account cannot read must not be silently replaced by one entry — that
            // would discard every other provider the real owner had configured.
            if (this._status == SecretReadStatus.Unreadable)
            {
                throw new InvalidOperationException(
                    this._unreadableReason ?? "The existing credentials could not be read.");
            }

            var next = this._cache is null
                ? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Entry>(this._cache, StringComparer.OrdinalIgnoreCase);

            next[api.Provider] = new Entry { Url = NullIfBlank(api.BaseUrl), Key = NullIfBlank(api.Key) };
            this.Persist(next);
        }
    }

    /// <summary>
    /// Removes one provider's stored entry, if present.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    public void Remove(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return;

        lock (this._gate)
        {
            this.Load();

            if (this._cache is null || !this._cache.ContainsKey(providerId))
                return;

            var next = new Dictionary<string, Entry>(this._cache, StringComparer.OrdinalIgnoreCase);
            next.Remove(providerId);
            this.Persist(next);
        }
    }

    /// <summary>
    /// Drops the cached read so the next access goes back to the store.
    /// </summary>
    public void Invalidate()
    {
        lock (this._gate)
        {
            this._cache = null;
            this._cachedAtUtc = DateTime.MinValue;
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Persist(Dictionary<string, Entry> entries)
    {
        var document = new Document { Version = CurrentVersion, Providers = entries };
        this._store.Write(JsonSerializer.Serialize(document, SerializerOptions));

        this._cache = entries;
        this._cachedAtUtc = DateTime.UtcNow;
        this._status = SecretReadStatus.Ok;
        this._unreadableReason = null;
    }

    // Reads through to the store when the cache is cold or stale. Never throws: a store that cannot
    // be read leaves the cache empty and a reason set, which every caller can render.
    private void Load()
    {
        if (this._cache is not null && (DateTime.UtcNow - this._cachedAtUtc).TotalSeconds < CacheSeconds)
            return;

        SecretReadResult result = this._store.Read();
        this._status = result.Status;
        this._unreadableReason = result.Status == SecretReadStatus.Unreadable ? result.Reason : null;
        this._cachedAtUtc = DateTime.UtcNow;

        if (result.Status != SecretReadStatus.Ok || result.Payload is null)
        {
            this._cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(result.Payload, SerializerOptions);
            this._cache = document?.Providers is null
                ? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Entry>(document.Providers, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            // Decryptable but not parseable — a truncated write, or a document from a future
            // version. Treat it as unreadable rather than empty for the same reason as a failed
            // decrypt: do not invite the user to overwrite something that may still hold their keys.
            this._cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            this._status = SecretReadStatus.Unreadable;
            this._unreadableReason = $"The stored credentials could not be parsed: {ex.Message}";
        }
    }

    // The serialized shape. Deliberately small and boring — it is written by one place and read by
    // one place, and a version field is cheaper than guessing later.
    private sealed class Document
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("providers")]
        public Dictionary<string, Entry>? Providers { get; set; }
    }

    private sealed class Entry
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }
    }
}
