// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Physalia.Core.Config.Secrets;

namespace Physalia.Core.Api;

/// <summary>
/// The HTTP APIs the user has configured, kept in Physalia's per-user data folder.
/// </summary>
/// <remarks>
/// <para><b>Its own file, not an extension of the provider catalog.</b> A provider is one of a
/// handful of endpoints the plug-in speaks the protocol of — a fixed list, shared as one vocabulary
/// by the credential store, the resolver, the bridge verbs and the UI. A user's REST API is the
/// other thing entirely: a third-party integration they discover, open-ended, exactly like an MCP
/// server. So this is shaped like <c>mcp-servers.json</c> rather than like <c>providers.json</c>,
/// and the provider catalog stays the small fixed table it was designed to be.</para>
/// <para><b>Plain, not encrypted</b>, for the reason <c>providers.json</c> and
/// <c>mcp-servers.json</c> are: an entry is a URL, a header name, and possibly the NAME of an
/// environment variable. The one secret — the key itself — is held in the encrypted credential store
/// under <see cref="ApiEndpoint.CredentialId"/>, so there is still exactly one encryption seam in
/// the repo.</para>
/// </remarks>
public sealed class ApiEndpointStore
{
    /// <summary>
    /// The file this is stored in, inside Physalia's per-user data folder.
    /// </summary>
    public const string FileName = "api-endpoints.json";

    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiEndpointStore"/> class.
    /// </summary>
    /// <param name="path">Absolute path of the JSON file backing this list.</param>
    public ApiEndpointStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path is required.", nameof(path));

        this._path = path;
    }

    /// <summary>
    /// Gets a store in Physalia's per-user data folder, beside the credential store.
    /// </summary>
    /// <returns>The endpoint store.</returns>
    public static ApiEndpointStore Default() =>
        new(Path.Combine(SecretStores.DataFolder(), FileName));

    /// <summary>
    /// Gets the absolute path of the backing file, for diagnostics.
    /// </summary>
    public string FilePath => this._path;

    /// <summary>
    /// Reads every configured endpoint.
    /// </summary>
    /// <returns>One entry per configured API, in file order; empty when nothing is configured.</returns>
    public IReadOnlyList<ApiEndpoint> Read()
    {
        lock (this._gate)
        {
            return this.ReadInternal();
        }
    }

    /// <summary>
    /// Finds one endpoint by name.
    /// </summary>
    /// <param name="name">The endpoint name, case-insensitive.</param>
    /// <returns>The entry, or null when no endpoint goes by that name.</returns>
    public ApiEndpoint? Find(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : this.Read().FirstOrDefault(e => NameMatches(e.Name, name!));

    /// <summary>
    /// Adds or replaces one endpoint.
    /// </summary>
    /// <param name="entry">The entry to store; its name is the key.</param>
    /// <param name="replacing">
    /// The entry's previous name when it is being renamed, so the old key is dropped rather than
    /// leaving a duplicate behind. Null or equal to the new name for an ordinary edit.
    /// </param>
    public void Save(ApiEndpoint entry, string? replacing = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Name))
            throw new ArgumentException("An endpoint name is required.", nameof(entry));

        lock (this._gate)
        {
            List<ApiEndpoint> entries = this.ReadInternal();

            // A rename drops the old key, and the row keeps its position either way: this list is
            // what the setup page shows, and an edit silently moving a row to the bottom reads as a
            // bug. Same rule, and the same reason, as the MCP server store.
            string oldName = string.IsNullOrWhiteSpace(replacing) ? entry.Name : replacing!;
            int at = entries.FindIndex(e => NameMatches(e.Name, oldName));

            entries.RemoveAll(e => NameMatches(e.Name, oldName) || NameMatches(e.Name, entry.Name));

            if (at >= 0 && at <= entries.Count)
                entries.Insert(at, entry);
            else
                entries.Add(entry);

            this.Write(entries);
        }
    }

    /// <summary>
    /// Removes one endpoint, if present.
    /// </summary>
    /// <param name="name">The endpoint name.</param>
    public void Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (this._gate)
        {
            List<ApiEndpoint> entries = this.ReadInternal();
            if (entries.RemoveAll(e => NameMatches(e.Name, name)) == 0)
                return;

            this.Write(entries);
        }
    }

    private static bool NameMatches(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Never throws. An unreadable or corrupt file means "nothing configured", which the setup page
    // already renders as its empty state — the same choice the activation list makes, and for the
    // same reason: there is nothing here a user could act on that re-adding the entry does not fix.
    private List<ApiEndpoint> ReadInternal()
    {
        var entries = new List<ApiEndpoint>();

        try
        {
            if (!File.Exists(this._path))
                return entries;

            string text = File.ReadAllText(this._path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
                return entries;

            Document? document = JsonSerializer.Deserialize<Document>(text, SerializerOptions);
            if (document?.ApiEndpoints is null)
                return entries;

            foreach (KeyValuePair<string, Entry> pair in document.ApiEndpoints)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                entries.Add(new ApiEndpoint(
                    pair.Key,
                    pair.Value.BaseUrl ?? string.Empty,
                    pair.Value.Auth,
                    pair.Value.AuthName ?? string.Empty,
                    pair.Value.AuthPrefix ?? string.Empty,
                    pair.Value.EnvVar ?? string.Empty));
            }
        }
        catch (Exception)
        {
            return new List<ApiEndpoint>();
        }

        return entries;
    }

    private void Write(IReadOnlyList<ApiEndpoint> entries)
    {
        try
        {
            string? dir = Path.GetDirectoryName(this._path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var document = new Document
            {
                Version = CurrentVersion,
                ApiEndpoints = entries.ToDictionary(
                    e => e.Name,
                    e => new Entry
                    {
                        BaseUrl = e.BaseUrl,
                        Auth = e.Auth,
                        AuthName = string.IsNullOrEmpty(e.AuthName) ? null : e.AuthName,
                        AuthPrefix = string.IsNullOrEmpty(e.AuthPrefix) ? null : e.AuthPrefix,
                        EnvVar = string.IsNullOrEmpty(e.EnvVar) ? null : e.EnvVar,
                    },
                    StringComparer.OrdinalIgnoreCase),
            };

            File.WriteAllText(this._path, JsonSerializer.Serialize(document, SerializerOptions), Encoding.UTF8);
        }
        catch (Exception)
        {
            // A read-only disk costs the user a re-entry next launch rather than the session they
            // are in; the in-memory read still serves every caller until then.
        }
    }

    private sealed class Document
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("apiEndpoints")]
        public Dictionary<string, Entry>? ApiEndpoints { get; set; }
    }

    private sealed class Entry
    {
        [JsonPropertyName("baseUrl")]
        public string? BaseUrl { get; set; }

        [JsonPropertyName("auth")]
        public ApiAuth Auth { get; set; }

        [JsonPropertyName("authName")]
        public string? AuthName { get; set; }

        [JsonPropertyName("authPrefix")]
        public string? AuthPrefix { get; set; }

        [JsonPropertyName("envVar")]
        public string? EnvVar { get; set; }
    }
}
