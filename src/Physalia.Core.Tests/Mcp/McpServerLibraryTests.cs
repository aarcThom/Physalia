// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Mcp;
using Xunit;

namespace Physalia.Core.Tests.Mcp;

public class McpServerLibraryTests
{
    [Fact]
    public void Parse_YamlWithFlowSequence_ReadsCommandAndArgs()
    {
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            mcpServers:
              filesystem:
                command: npx
                args: ["-y", "@modelcontextprotocol/server-filesystem", "C:/refs"]
            """);

        McpServerDefinition server = Assert.Single(servers);
        Assert.Equal("filesystem", server.Name);
        Assert.Equal("npx", server.Command);
        Assert.Equal(new[] { "-y", "@modelcontextprotocol/server-filesystem", "C:/refs" }, server.Arguments);
        Assert.False(server.IsRemote);
        Assert.True(server.IsRunnable);
    }

    [Fact]
    public void Parse_YamlWithBlockSequence_ReadsArgs()
    {
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            mcpServers:
              git:
                command: uvx
                args:
                  - mcp-server-git
                  - "--repository"
                  - C:/repo
            """);

        Assert.Equal(new[] { "mcp-server-git", "--repository", "C:/repo" }, Assert.Single(servers).Arguments);
    }

    [Fact]
    public void Parse_SeveralServers_KeepsEachSeparate()
    {
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            mcpServers:
              alpha:
                command: a
                args: ["1"]
              beta:
                url: https://example.com/mcp
              gamma:
                command: c
                cwd: C:/work
            """);

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, servers.Select(s => s.Name));
        Assert.Equal(new[] { "1" }, servers[0].Arguments);
        Assert.True(servers[1].IsRemote);
        Assert.Empty(servers[1].Arguments);
        Assert.Equal("C:/work", servers[2].WorkingDirectory);
    }

    [Fact]
    public void Parse_EnvBlock_ReadsPairsAndDoesNotLeakIntoTheNextServer()
    {
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            mcpServers:
              github:
                command: npx
                env:
                  TOKEN: abc123
                  OTHER: xyz
              plain:
                command: p
            """);

        Assert.Equal(2, servers[0].Environment.Count);
        Assert.Equal("abc123", servers[0].Environment["TOKEN"]);
        Assert.Empty(servers[1].Environment);
    }

    [Fact]
    public void Parse_Comments_AreIgnored()
    {
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            # leading comment
            mcpServers:
              # about this server
              alpha:
                command: a   # trailing comment
            """);

        Assert.Equal("a", Assert.Single(servers).Command);
    }

    [Fact]
    public void Parse_JsonForm_ReadsTheSameShape()
    {
        // A claude_desktop_config.json pasted in wholesale must work: that is the whole point of
        // using the standard block rather than inventing a format.
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            {"mcpServers":{"filesystem":{"command":"npx","args":["-y","pkg"],"env":{"K":"V"}}}}
            """);

        McpServerDefinition server = Assert.Single(servers);
        Assert.Equal("filesystem", server.Name);
        Assert.Equal("npx", server.Command);
        Assert.Equal(new[] { "-y", "pkg" }, server.Arguments);
        Assert.Equal("V", server.Environment["K"]);
    }

    [Fact]
    public void Parse_UnknownKeys_AreIgnoredNotRejected()
    {
        // One config file may be shared with another MCP host that understands more keys.
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            mcpServers:
              alpha:
                command: a
                disabled: false
                type: stdio
                autoApprove: ["x"]
            """);

        Assert.Equal("a", Assert.Single(servers).Command);
    }

    [Fact]
    public void Parse_Garbage_ReturnsEmptyRatherThanThrowing()
    {
        Assert.Empty(McpServerLibrary.Parse("{ this is not json"));
        Assert.Empty(McpServerLibrary.Parse(string.Empty));
        Assert.Empty(McpServerLibrary.Parse("   "));
    }

    [Fact]
    public void ExpandEnvironment_SubstitutesSetVariables()
    {
        Environment.SetEnvironmentVariable("PHYSALIA_TEST_TOKEN", "secret");
        try
        {
            Assert.Equal("secret", McpServerLibrary.ExpandEnvironment("${PHYSALIA_TEST_TOKEN}"));
            Assert.Equal("a-secret-b", McpServerLibrary.ExpandEnvironment("a-${PHYSALIA_TEST_TOKEN}-b"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSALIA_TEST_TOKEN", null);
        }
    }

    [Fact]
    public void ExpandEnvironment_UnsetVariable_KeepsTheLiteral()
    {
        // Blanking it would look like a configured-but-empty credential, which is far harder to
        // diagnose than a server rejecting the literal "${...}".
        Assert.Equal("${NOT_SET_ANYWHERE_XYZ}", McpServerLibrary.ExpandEnvironment("${NOT_SET_ANYWHERE_XYZ}"));
    }

    [Fact]
    public void Identity_DiffersWhenAnyLaunchDetailChanges()
    {
        McpServerDefinition Make(string command, string arg, string token) => new(
            "same-name",
            command,
            new[] { arg },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["T"] = token },
            WorkingDirectory: null,
            Url: null,
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Scope: null);

        string baseline = Make("npx", "a", "t1").Identity;

        Assert.Equal(baseline, Make("npx", "a", "t1").Identity);
        Assert.NotEqual(baseline, Make("node", "a", "t1").Identity);
        Assert.NotEqual(baseline, Make("npx", "b", "t1").Identity);

        // A changed credential MUST fork the pool: a warm process cannot re-authenticate.
        Assert.NotEqual(baseline, Make("npx", "a", "t2").Identity);
    }

    [Fact]
    public void Identity_IgnoresTheEntryName()
    {
        // Two entries differing only in their key are the same server; launching it twice would
        // orphan a process.
        McpServerDefinition Make(string name) => new(
            name,
            "npx",
            new[] { "a" },
            new Dictionary<string, string>(StringComparer.Ordinal),
            WorkingDirectory: null,
            Url: null,
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Scope: null);

        Assert.Equal(Make("one").Identity, Make("two").Identity);
    }

    [Fact]
    public void IsRunnable_FalseWhenNeitherCommandNorUrl()
    {
        Assert.False(McpServerDefinition.Unrunnable("broken").IsRunnable);
    }

    [Fact]
    public void Parse_YamlHeadersAndScope_ReadOntoARemoteEntry()
    {
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            mcpServers:
              hosted:
                url: https://example.com/mcp
                scope: read write
                headers:
                  Authorization: Bearer abc123
                  X-Tenant: acme
              plain:
                command: p
            """);

        McpServerDefinition hosted = servers[0];
        Assert.Equal("Bearer abc123", hosted.Headers["Authorization"]);
        Assert.Equal("acme", hosted.Headers["X-Tenant"]);
        Assert.Equal("read write", hosted.Scope);

        // Same leak check the env block gets: an open map field must close at the next entry.
        Assert.Empty(servers[1].Headers);
        Assert.Null(servers[1].Scope);
    }

    [Fact]
    public void Parse_Headers_AreCaseInsensitive()
    {
        // HTTP header names are case-insensitive, so a config written `authorization:` must reach
        // the same slot as one written `Authorization:`.
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            mcpServers:
              hosted:
                url: https://example.com/mcp
                headers:
                  authorization: Bearer t
            """);

        Assert.Equal("Bearer t", Assert.Single(servers).Headers["AUTHORIZATION"]);
    }

    [Fact]
    public void Parse_HeadersExpandEnvironmentReferences()
    {
        Environment.SetEnvironmentVariable("PHYSALIA_TEST_HEADER", "secret");
        try
        {
            IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
                mcpServers:
                  hosted:
                    url: https://example.com/mcp
                    headers:
                      Authorization: Bearer ${PHYSALIA_TEST_HEADER}
                """);

            Assert.Equal("Bearer secret", Assert.Single(servers).Headers["Authorization"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSALIA_TEST_HEADER", null);
        }
    }

    [Fact]
    public void Parse_JsonHeadersAndScope_ReadTheSameShape()
    {
        IReadOnlyList<McpServerDefinition> servers = McpServerLibrary.Parse("""
            {"mcpServers":{"hosted":{"url":"https://e.com/mcp","scope":"read","headers":{"Authorization":"Bearer j"}}}}
            """);

        McpServerDefinition server = Assert.Single(servers);
        Assert.True(server.IsRemote);
        Assert.Equal("Bearer j", server.Headers["Authorization"]);
        Assert.Equal("read", server.Scope);
    }

    [Fact]
    public void Identity_DiffersWhenAHeaderOrScopeChanges()
    {
        McpServerDefinition Make(string token, string? scope) => new(
            "hosted",
            Command: null,
            Arguments: Array.Empty<string>(),
            Environment: new Dictionary<string, string>(StringComparer.Ordinal),
            WorkingDirectory: null,
            Url: "https://example.com/mcp",
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = token },
            Scope: scope);

        string baseline = Make("t1", "read").Identity;

        Assert.Equal(baseline, Make("t1", "read").Identity);

        // A changed credential MUST fork the pool, exactly as a changed env value does: the warm
        // bridge process authenticated with the old token.
        Assert.NotEqual(baseline, Make("t2", "read").Identity);
        Assert.NotEqual(baseline, Make("t1", "read write").Identity);
    }
}
