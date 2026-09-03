// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Mcp;
using Xunit;

namespace Physalia.Core.Tests.Mcp;

public class McpConfigEditorTests
{
    private static McpServerDefinition Local(
        string name,
        string command,
        params string[] args) => new(
        name,
        command,
        args,
        new Dictionary<string, string>(StringComparer.Ordinal),
        WorkingDirectory: null,
        Url: null,
        Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Scope: null);

    private static McpServerDefinition Remote(
        string name,
        string url,
        IReadOnlyDictionary<string, string>? headers = null,
        string? scope = null) => new(
        name,
        Command: null,
        Arguments: Array.Empty<string>(),
        Environment: new Dictionary<string, string>(StringComparer.Ordinal),
        WorkingDirectory: null,
        Url: url,
        Headers: headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Scope: scope);

    [Fact]
    public void Upsert_NewEntry_IsReadBackByTheParser()
    {
        string content = McpConfigEditor.Upsert("mcpServers:\n", Local("fs", "npx", "-y", "pkg"));

        McpServerDefinition server = Assert.Single(McpServerLibrary.Parse(content));
        Assert.Equal("fs", server.Name);
        Assert.Equal("npx", server.Command);
        Assert.Equal(new[] { "-y", "pkg" }, server.Arguments);
    }

    [Fact]
    public void Upsert_RemoteEntry_RoundTripsHeadersAndScope()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer ${TOKEN}",
        };

        string content = McpConfigEditor.Upsert(
            "mcpServers:\n",
            Remote("hosted", "https://example.com/mcp", headers, "read write"));

        McpServerDefinition server = Assert.Single(McpConfigEditor.ParseRaw(content));
        Assert.Equal("https://example.com/mcp", server.Url);
        Assert.Equal("Bearer ${TOKEN}", server.Headers["Authorization"]);
        Assert.Equal("read write", server.Scope);
    }

    [Fact]
    public void ParseRaw_LeavesEnvironmentReferencesUnexpanded()
    {
        // The whole reason the editor has its own read path: showing the RESOLVED token in the form
        // would write it back into the file on the next save, defeating the ${...} entirely.
        Environment.SetEnvironmentVariable("PHYSALIA_TEST_EDITOR", "s3cret");
        try
        {
            const string Content = """
                mcpServers:
                  hosted:
                    url: https://example.com/mcp
                    headers:
                      Authorization: Bearer ${PHYSALIA_TEST_EDITOR}
                """;

            Assert.Equal(
                "Bearer ${PHYSALIA_TEST_EDITOR}",
                Assert.Single(McpConfigEditor.ParseRaw(Content)).Headers["Authorization"]);

            // The connection path still expands, or nothing would authenticate.
            Assert.Equal(
                "Bearer s3cret",
                Assert.Single(McpServerLibrary.Parse(Content)).Headers["Authorization"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSALIA_TEST_EDITOR", null);
        }
    }

    [Fact]
    public void Upsert_KeepsEveryOtherLineByteForByte()
    {
        // The point of editing rather than regenerating: the shipped file is mostly commentary, and
        // a user's own notes must survive a save from the UI.
        const string Content = """
            # Physalia — MCP servers
            # A long explanatory header the user may have edited.

            mcpServers:

              # my notes about alpha
              alpha:
                command: a

              # my notes about beta
              beta:
                command: b
            """;

        string updated = McpConfigEditor.Upsert(Content, Local("alpha", "changed"));

        Assert.Contains("# Physalia — MCP servers", updated, StringComparison.Ordinal);
        Assert.Contains("# A long explanatory header the user may have edited.", updated, StringComparison.Ordinal);
        Assert.Contains("# my notes about alpha", updated, StringComparison.Ordinal);
        Assert.Contains("# my notes about beta", updated, StringComparison.Ordinal);

        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse(updated);
        Assert.Equal(new[] { "alpha", "beta" }, servers.Select(s => s.Name));
        Assert.Equal("changed", servers[0].Command);
        Assert.Equal("b", servers[1].Command);
    }

    [Fact]
    public void Upsert_ExistingEntry_ReplacesItInPlaceAndDropsStaleFields()
    {
        const string Content = """
            mcpServers:
              alpha:
                command: a
                cwd: C:/old
                env:
                  K: V
              beta:
                command: b
            """;

        string updated = McpConfigEditor.Upsert(Content, Local("alpha", "a2"));
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse(updated);

        // Order preserved — an edit must not move the entry to the top.
        Assert.Equal(new[] { "alpha", "beta" }, servers.Select(s => s.Name));

        // The old cwd/env are gone: the entry is REPLACED, not merged into.
        Assert.Equal("a2", servers[0].Command);
        Assert.Null(servers[0].WorkingDirectory);
        Assert.Empty(servers[0].Environment);

        // And the neighbour is untouched.
        Assert.Equal("b", servers[1].Command);
    }

    [Fact]
    public void Upsert_Rename_ReplacesInPlaceRatherThanAddingASecondEntry()
    {
        const string Content = """
            mcpServers:
              alpha:
                command: a
              beta:
                command: b
            """;

        string updated = McpConfigEditor.Upsert(Content, Local("renamed", "a"), replacing: "alpha");
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse(updated);

        Assert.Equal(new[] { "renamed", "beta" }, servers.Select(s => s.Name));
    }

    [Fact]
    public void Remove_TakesTheEntryAndLeavesTheCommentBelowIt()
    {
        // A comment sits ABOVE the thing it describes, so an entry's span must stop before the next
        // entry's comment — otherwise deleting alpha silently deletes beta's documentation.
        const string Content = """
            mcpServers:
              alpha:
                command: a

              # what beta is for
              beta:
                command: b
            """;

        string updated = McpConfigEditor.Remove(Content, "alpha");

        Assert.Contains("# what beta is for", updated, StringComparison.Ordinal);
        Assert.Equal("beta", Assert.Single(McpServerLibrary.Parse(updated)).Name);
    }

    [Fact]
    public void Remove_UnknownName_ReturnsContentUnchanged()
    {
        const string Content = "mcpServers:\n  alpha:\n    command: a\n";
        Assert.Equal(Content, McpConfigEditor.Remove(Content, "nope"));
    }

    [Fact]
    public void Upsert_IntoAFileWithNoBlock_StartsOneAndKeepsWhatWasThere()
    {
        string updated = McpConfigEditor.Upsert("# just a comment\n", Local("fs", "npx"));

        Assert.Contains("# just a comment", updated, StringComparison.Ordinal);
        Assert.Equal("fs", Assert.Single(McpServerLibrary.Parse(updated)).Name);
    }

    [Fact]
    public void Upsert_IntoAnEmptyFile_ProducesAReadableBlock()
    {
        Assert.Equal("fs", Assert.Single(McpServerLibrary.Parse(McpConfigEditor.Upsert(string.Empty, Local("fs", "npx")))).Name);
    }

    [Fact]
    public void Upsert_MatchesTheFilesOwnIndentation()
    {
        // A file written with 4-space entries must not come back half-converted to 2.
        const string Content = """
            mcpServers:
                alpha:
                    command: a
            """;

        string updated = McpConfigEditor.Upsert(Content, Local("beta", "b"));

        Assert.Contains("    beta:", updated, StringComparison.Ordinal);
        Assert.Equal(new[] { "beta", "alpha" }, McpServerLibrary.Parse(updated).Select(s => s.Name));
    }

    [Fact]
    public void Upsert_AwkwardValues_SurviveTheRoundTrip()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // A value the reader would otherwise truncate at the ' #'.
            ["X-Note"] = "hash # inside",
        };

        string content = McpConfigEditor.Upsert(
            "mcpServers:\n",
            Remote("hosted", "https://example.com/mcp?a=1&b=2", headers));

        McpServerDefinition server = Assert.Single(McpConfigEditor.ParseRaw(content));
        Assert.Equal("https://example.com/mcp?a=1&b=2", server.Url);
        Assert.Equal("hash # inside", server.Headers["X-Note"]);
    }

    [Fact]
    public void Upsert_ArgumentsThatLookLikeYaml_SurviveTheRoundTrip()
    {
        string content = McpConfigEditor.Upsert(
            "mcpServers:\n",
            Local("odd", "npx", "--flag", "C:/path with spaces/x", "[bracketed]", "trailing # hash"));

        Assert.Equal(
            new[] { "--flag", "C:/path with spaces/x", "[bracketed]", "trailing # hash" },
            Assert.Single(McpConfigEditor.ParseRaw(content)).Arguments);
    }

    [Fact]
    public void Upsert_IntoTheShippedShape_LandsAboveTheCommentedExamples()
    {
        // The first-run case, and the one most likely to be hit: MCP_SERVERS.YAML starts life as a
        // copy of the shipped .example, whose mcpServers block holds nothing but commented-out
        // samples. There is no real entry to take the indentation from, and the new one must go
        // above the commentary rather than be buried in it.
        const string Content = """
            # Physalia — MCP servers
            #
            # Copy this file to MCP_SERVERS.YAML and list the servers you want.

            mcpServers:

              # ------------------------------------------------ local (stdio) servers
              # filesystem:
              #   command: npx
              #   args: ["-y", "@modelcontextprotocol/server-filesystem", "C:/refs"]

              # ------------------------------------------------ remote (HTTP) servers
              # notion:
              #   url: https://mcp.notion.com/mcp
            """;

        string updated = McpConfigEditor.Upsert(Content, Local("fs", "npx", "-y", "pkg"));

        McpServerDefinition server = Assert.Single(McpServerLibrary.Parse(updated));
        Assert.Equal("fs", server.Name);
        Assert.Equal(new[] { "-y", "pkg" }, server.Arguments);

        // Every comment survives, including the commented-out samples that teach the format.
        Assert.Contains("# Physalia — MCP servers", updated, StringComparison.Ordinal);
        Assert.Contains("#   url: https://mcp.notion.com/mcp", updated, StringComparison.Ordinal);

        // And the entry sits directly under the block header, not after the sample block.
        string[] lines = updated.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int header = Array.FindIndex(lines, l => l.Trim().StartsWith("mcpServers:", StringComparison.Ordinal));
        Assert.Equal("  fs:", lines[header + 1]);
    }

    [Fact]
    public void Upsert_ThenRemove_LeavesTheFileReadableAndTheCommentsIntact()
    {
        const string Content = """
            # header

            mcpServers:

              # a sample
              # alpha:
              #   command: a
            """;

        string added = McpConfigEditor.Upsert(Content, Local("fs", "npx"));
        string removed = McpConfigEditor.Remove(added, "fs");

        Assert.Empty(McpServerLibrary.Parse(removed));
        Assert.Contains("# header", removed, StringComparison.Ordinal);
        Assert.Contains("#   command: a", removed, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeWriteBlock_JsonForm_IsRefusedRatherThanRewritten()
    {
        // A pasted claude_desktop_config.json is shared with another host; converting it to YAML
        // behind the user's back would break that.
        const string Json = """{"mcpServers":{"fs":{"command":"npx"}}}""";

        Assert.NotNull(McpConfigEditor.DescribeWriteBlock(Json));
        Assert.Equal("fs", Assert.Single(McpConfigEditor.ParseRaw(Json)).Name);
    }

    [Fact]
    public void DescribeWriteBlock_OrdinaryYaml_IsEditable()
    {
        Assert.Null(McpConfigEditor.DescribeWriteBlock("mcpServers:\n  alpha:\n    command: a\n"));
        Assert.Null(McpConfigEditor.DescribeWriteBlock(string.Empty));
    }

    [Fact]
    public void DescribeWriteBlock_ServersWithNoWrapper_AreRefused()
    {
        // The parser tolerates this shape; the writer cannot, because nothing distinguishes a server
        // key from any other top-level key.
        Assert.NotNull(McpConfigEditor.DescribeWriteBlock("alpha:\n  command: a\n"));
    }
}
