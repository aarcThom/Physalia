// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Physalia.Core.Config.Secrets;

namespace Physalia.Core.Mcp;

/// <summary>
/// The MCP servers the user has configured, kept in Physalia's per-user data folder.
/// </summary>
/// <remarks>
/// <para><b>Replaces <c>MCP_SERVERS.YAML</c> outright</b> (2026-09-05). That file was a hand-edited
/// document, and the machinery around it — an in-place editor that preserved comments, indentation
/// and entry order, a read-only mode for the JSON form, a shipped template teaching the
/// <c>${VAR}</c> convention — all existed to protect authoring that no longer happens. The chat
/// window's "Configure MCP connections" page owns this now, so the same argument that moved the
/// provider credentials applies: what the machine writes needs no commentary, only a shape.</para>
/// <para><b>Stored as the standard <c>mcpServers</c> block</b>, so what is on disk is still what
/// every other MCP host uses — a <c>claude_desktop_config.json</c> pastes in whole through
/// <see cref="Import"/>, and the file can be lifted out and used elsewhere. What went away is YAML
/// and the pretence that anyone would hand-edit it.</para>
/// <para><b>Plain, not encrypted.</b> An entry is mostly a command, its arguments and a URL, and
/// <c>${VAR}</c> exists precisely so a credential need never be written down — the same reasoning
/// that keeps <c>providers.json</c> readable. A token pasted inline is stored inline, which is why
/// the setup page reads raw values and never writes back an expanded one.</para>
/// </remarks>
public sealed class McpServerStore
{
    /// <summary>
    /// The file this is stored in, inside Physalia's per-user data folder.
    /// </summary>
    public const string FileName = "mcp-servers.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerStore"/> class.
    /// </summary>
    /// <param name="path">Absolute path of the JSON file backing this list.</param>
    public McpServerStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path is required.", nameof(path));

        this._path = path;
    }

    /// <summary>
    /// Gets a store in Physalia's per-user data folder, beside the credential store.
    /// </summary>
    /// <returns>The server store.</returns>
    public static McpServerStore Default() =>
        new(Path.Combine(SecretStores.DataFolder(), FileName));

    /// <summary>
    /// Gets the absolute path of the backing file, for change-watching and diagnostics.
    /// </summary>
    public string FilePath => this._path;

    /// <summary>
    /// Reads every configured server, with <c>${VAR}</c> references resolved.
    /// </summary>
    /// <remarks>This is what a CONNECTION needs. Use <see cref="ReadRaw"/> to populate an editor.</remarks>
    /// <returns>One definition per entry; empty when nothing is configured.</returns>
    public IReadOnlyList<McpServerDefinition> Read() => this.ReadInternal(expandEnvironment: true);

    /// <summary>
    /// Reads every configured server exactly as written, references unresolved.
    /// </summary>
    /// <remarks>
    /// What an EDITOR needs. Populating a form from expanded values and saving it back would bake
    /// the resolved secret into the file that <c>${VAR}</c> existed to keep it out of — a silent
    /// credential leak on the user's next unrelated edit.
    /// </remarks>
    /// <returns>One definition per entry, unexpanded.</returns>
    public IReadOnlyList<McpServerDefinition> ReadRaw() => this.ReadInternal(expandEnvironment: false);

    /// <summary>
    /// Adds or replaces one server entry.
    /// </summary>
    /// <param name="entry">The entry to store; its name is the key.</param>
    /// <param name="replacing">
    /// The entry's previous name when it is being renamed, so the old key is dropped rather than
    /// leaving a duplicate behind. Null or equal to the new name for an ordinary edit.
    /// </param>
    public void Save(McpServerDefinition entry, string? replacing = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Name))
            throw new ArgumentException("A server name is required.", nameof(entry));

        lock (this._gate)
        {
            List<McpServerDefinition> entries = this.ReadInternal(expandEnvironment: false).ToList();

            // A rename drops the old key. Position is preserved either way: the list is what the
            // setup page shows, and an edit silently moving a row to the bottom reads as a bug.
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
    /// Removes one server entry, if present.
    /// </summary>
    /// <param name="name">The entry's name.</param>
    public void Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (this._gate)
        {
            List<McpServerDefinition> entries = this.ReadInternal(expandEnvironment: false).ToList();
            if (entries.RemoveAll(e => NameMatches(e.Name, name)) == 0)
                return;

            this.Write(entries);
        }
    }

    /// <summary>
    /// Merges a pasted configuration — a whole <c>mcpServers</c> block, or another host's
    /// <c>claude_desktop_config.json</c> — into the store.
    /// </summary>
    /// <param name="content">The pasted JSON (or legacy YAML) text.</param>
    /// <returns>The names of the entries that were added or replaced.</returns>
    public IReadOnlyList<string> Import(string content)
    {
        IReadOnlyList<McpServerDefinition> incoming =
            McpServerLibrary.Parse(content, expandEnvironment: false);

        if (incoming.Count == 0)
            return Array.Empty<string>();

        lock (this._gate)
        {
            List<McpServerDefinition> entries = this.ReadInternal(expandEnvironment: false).ToList();

            foreach (McpServerDefinition entry in incoming)
            {
                entries.RemoveAll(e => NameMatches(e.Name, entry.Name));
                entries.Add(entry);
            }

            this.Write(entries);
        }

        return incoming.Select(e => e.Name).ToList();
    }

    /// <summary>
    /// Imports a legacy <c>MCP_SERVERS.YAML</c> once, then deletes it.
    /// </summary>
    /// <remarks>
    /// Deleting is right here where it was wrong for the API-key YAML: this file has already been
    /// read into a store that supersedes it, and leaving it would mean two lists of servers — with
    /// credentials in the stale one — that nothing keeps in step. Never throws; a failure leaves the
    /// YAML alone and the user re-adds their servers.
    /// </remarks>
    /// <param name="legacyPath">Absolute path to the old YAML file.</param>
    /// <returns>The names imported; empty when there was nothing to do.</returns>
    public IReadOnlyList<string> ImportLegacyFile(string legacyPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath))
                return Array.Empty<string>();

            IReadOnlyList<string> imported = this.Import(File.ReadAllText(legacyPath, Encoding.UTF8));

            // Delete ONLY once something actually came across. An unparseable file yields nothing,
            // and removing it then would destroy the only copy of a list we failed to read — the
            // difference between a migration and a deletion.
            if (imported.Count > 0)
                File.Delete(legacyPath);

            return imported;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static bool NameMatches(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string>? OrNull(IReadOnlyDictionary<string, string> map) =>
        map.Count == 0 ? null : map.ToDictionary(kv => kv.Key, kv => kv.Value);

    private static List<string>? OrNull(IReadOnlyList<string> list) =>
        list.Count == 0 ? null : list.ToList();

    private IReadOnlyList<McpServerDefinition> ReadInternal(bool expandEnvironment)
    {
        try
        {
            if (!File.Exists(this._path))
                return Array.Empty<McpServerDefinition>();

            string text = File.ReadAllText(this._path, Encoding.UTF8);

            // Reuses the shared parser rather than a second reader: what is on disk IS the standard
            // mcpServers block, which is the whole point of storing it in that shape.
            return McpServerLibrary.Parse(text, expandEnvironment);
        }
        catch (Exception)
        {
            return Array.Empty<McpServerDefinition>();
        }
    }

    private void Write(IReadOnlyList<McpServerDefinition> entries)
    {
        var servers = new Dictionary<string, Entry>(StringComparer.Ordinal);

        foreach (McpServerDefinition e in entries)
        {
            servers[e.Name] = new Entry
            {
                Command = e.Command,
                Args = OrNull(e.Arguments),
                Cwd = e.WorkingDirectory,
                Env = OrNull(e.Environment),
                Url = e.Url,
                Headers = OrNull(e.Headers),
                Scope = e.Scope,
            };
        }

        string? dir = Path.GetDirectoryName(this._path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(
            this._path,
            JsonSerializer.Serialize(new Document { McpServers = servers }, SerializerOptions),
            Encoding.UTF8);
    }

    // The standard mcpServers document, which is deliberately NOT a Physalia-shaped envelope: no
    // version field, no wrapper. Anything that reads an MCP config reads this.
    private sealed class Document
    {
        [JsonPropertyName("mcpServers")]
        public Dictionary<string, Entry>? McpServers { get; set; }
    }

    private sealed class Entry
    {
        [JsonPropertyName("command")]
        public string? Command { get; set; }

        [JsonPropertyName("args")]
        public List<string>? Args { get; set; }

        [JsonPropertyName("cwd")]
        public string? Cwd { get; set; }

        [JsonPropertyName("env")]
        public Dictionary<string, string>? Env { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}
