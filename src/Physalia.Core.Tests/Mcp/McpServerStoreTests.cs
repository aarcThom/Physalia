// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Physalia.Core.Mcp;
using Xunit;

namespace Physalia.Core.Tests.Mcp;

/// <summary>
/// Covers the MCP server store that replaced <c>MCP_SERVERS.YAML</c>.
/// </summary>
public class McpServerStoreTests : IDisposable
{
    private const string EnvVar = "PHY_TEST_MCP_TOKEN";

    private readonly string? _savedEnv = Environment.GetEnvironmentVariable(EnvVar);
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, this._savedEnv);

        foreach (string path in this._tempFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void An_absent_store_reads_as_empty()
    {
        Assert.Empty(this.NewStore().Read());
    }

    [Fact]
    public void Save_then_read_round_trips_a_local_server()
    {
        McpServerStore store = this.NewStore();
        store.Save(Local("filesystem", "npx", "-y", "@modelcontextprotocol/server-filesystem"));

        McpServerDefinition read = Assert.Single(store.Read());
        Assert.Equal("filesystem", read.Name);
        Assert.Equal("npx", read.Command);
        Assert.Equal(new[] { "-y", "@modelcontextprotocol/server-filesystem" }, read.Arguments);
        Assert.False(read.IsRemote);
    }

    [Fact]
    public void Save_then_read_round_trips_a_remote_server()
    {
        McpServerStore store = this.NewStore();
        store.Save(new McpServerDefinition(
            "notion", null, Array.Empty<string>(), new Dictionary<string, string>(), null,
            "https://mcp.notion.com/mcp",
            new Dictionary<string, string> { ["Authorization"] = "Bearer abc" },
            "read"));

        McpServerDefinition read = Assert.Single(store.Read());
        Assert.True(read.IsRemote);
        Assert.Equal("https://mcp.notion.com/mcp", read.Url);
        Assert.Equal("Bearer abc", read.Headers["Authorization"]);
        Assert.Equal("read", read.Scope);
    }

    [Fact]
    public void Editing_an_entry_keeps_its_position()
    {
        // The list IS the setup page's ordering; an edit quietly moving a row to the bottom reads as
        // a bug to whoever is looking at it.
        McpServerStore store = this.NewStore();
        store.Save(Local("alpha", "a"));
        store.Save(Local("beta", "b"));
        store.Save(Local("gamma", "c"));

        store.Save(Local("alpha", "changed"));

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, store.Read().Select(d => d.Name));
        Assert.Equal("changed", store.Read().First().Command);
    }

    [Fact]
    public void A_rename_drops_the_old_key_rather_than_duplicating()
    {
        McpServerStore store = this.NewStore();
        store.Save(Local("alpha", "a"));
        store.Save(Local("beta", "b"));

        store.Save(Local("renamed", "a"), replacing: "alpha");

        Assert.Equal(new[] { "renamed", "beta" }, store.Read().Select(d => d.Name));
    }

    [Fact]
    public void Remove_drops_only_that_entry()
    {
        McpServerStore store = this.NewStore();
        store.Save(Local("alpha", "a"));
        store.Save(Local("beta", "b"));

        store.Remove("alpha");

        Assert.Equal(new[] { "beta" }, store.Read().Select(d => d.Name));
    }

    [Fact]
    public void Read_expands_environment_references_but_ReadRaw_does_not()
    {
        // The distinction the editor depends on: showing an expanded value in the form and saving it
        // back would bake the resolved secret into the store that ${VAR} existed to keep it out of.
        Environment.SetEnvironmentVariable(EnvVar, "secret-token");

        McpServerStore store = this.NewStore();
        store.Save(new McpServerDefinition(
            "remote", null, Array.Empty<string>(), new Dictionary<string, string>(), null,
            "https://example.test/mcp",
            new Dictionary<string, string> { ["Authorization"] = "Bearer ${" + EnvVar + "}" },
            null));

        Assert.Equal("Bearer secret-token", store.Read().Single().Headers["Authorization"]);
        Assert.Equal("Bearer ${" + EnvVar + "}", store.ReadRaw().Single().Headers["Authorization"]);
    }

    [Fact]
    public void A_saved_reference_stays_a_reference_across_an_unrelated_edit()
    {
        // The leak this guards: edit server B, and server A's token must not have been resolved and
        // written down as a side effect.
        Environment.SetEnvironmentVariable(EnvVar, "secret-token");

        McpServerStore store = this.NewStore();
        store.Save(new McpServerDefinition(
            "a", "npx", Array.Empty<string>(),
            new Dictionary<string, string> { ["TOKEN"] = "${" + EnvVar + "}" },
            null, null, new Dictionary<string, string>(), null));
        store.Save(Local("b", "node"));

        store.Save(Local("b", "deno"));

        Assert.Equal("${" + EnvVar + "}", store.ReadRaw().First().Environment["TOKEN"]);
    }

    [Fact]
    public void Import_merges_a_pasted_mcpServers_block()
    {
        McpServerStore store = this.NewStore();
        store.Save(Local("existing", "node"));

        IReadOnlyList<string> imported = store.Import(
            """{"mcpServers":{"fs":{"command":"npx","args":["-y","pkg"]},"web":{"url":"https://x.test/mcp"}}}""");

        Assert.Equal(new[] { "fs", "web" }, imported);
        Assert.Equal(new[] { "existing", "fs", "web" }, store.Read().Select(d => d.Name));
    }

    [Fact]
    public void Import_replaces_an_entry_of_the_same_name()
    {
        McpServerStore store = this.NewStore();
        store.Save(Local("fs", "old"));

        store.Import("""{"mcpServers":{"fs":{"command":"new"}}}""");

        Assert.Equal("new", Assert.Single(store.Read()).Command);
    }

    [Fact]
    public void A_legacy_yaml_is_imported_once_and_then_deleted()
    {
        // Deleting is right here: the file has been read into a store that supersedes it, and two
        // lists of servers — credentials in the stale one — is what nothing keeps in step.
        string yaml = this.TempPath(".yaml");
        File.WriteAllText(
            yaml,
            "mcpServers:\n  everything:\n    command: npx\n    args:\n      - -y\n      - server-everything\n");

        McpServerStore store = this.NewStore();
        Assert.Equal(new[] { "everything" }, store.ImportLegacyFile(yaml));

        Assert.False(File.Exists(yaml));
        Assert.Equal("npx", Assert.Single(store.Read()).Command);
        Assert.Empty(store.ImportLegacyFile(yaml));
    }

    [Fact]
    public void An_unreadable_legacy_file_is_left_alone_rather_than_deleted()
    {
        // The difference between a migration and a deletion: nothing came across, so the only copy
        // of that list must survive for a human to look at.
        string yaml = this.TempPath(".yaml");
        File.WriteAllText(yaml, "this is not a server list at all\n");

        Assert.Empty(this.NewStore().ImportLegacyFile(yaml));
        Assert.True(File.Exists(yaml));
    }

    [Fact]
    public void What_lands_on_disk_is_the_standard_mcpServers_block()
    {
        // Deliberately not a Physalia-shaped envelope: the file stays something any MCP host reads,
        // and something the user can lift out and take elsewhere.
        McpServerStore store = this.NewStore(out string path);
        store.Save(Local("fs", "npx", "-y"));

        string json = File.ReadAllText(path);
        Assert.Contains("\"mcpServers\"", json);
        Assert.DoesNotContain("\"version\"", json);

        // And it round-trips through the shared parser, which is what makes that claim true.
        Assert.Equal("fs", Assert.Single(McpServerLibrary.Parse(json)).Name);
    }

    private static McpServerDefinition Local(string name, string command, params string[] args) =>
        new(name, command, args, new Dictionary<string, string>(), null, null,
            new Dictionary<string, string>(), null);

    private string TempPath(string extension = ".json")
    {
        string path = Path.Combine(Path.GetTempPath(), $"phy-mcp-{Guid.NewGuid():N}{extension}");
        this._tempFiles.Add(path);
        return path;
    }

    private McpServerStore NewStore() => this.NewStore(out _);

    private McpServerStore NewStore(out string path)
    {
        path = this.TempPath();
        return new McpServerStore(path);
    }
}
