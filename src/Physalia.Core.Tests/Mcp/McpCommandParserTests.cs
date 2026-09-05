// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
using Physalia.Core.Mcp;
using Xunit;

namespace Physalia.Core.Tests.Mcp;

public class McpCommandParserTests
{
    // The token Illustrator's preferences pane hands out, shortened. Both real commands below were
    // copied from that pane, which is the case this whole path exists for.
    private const string Token = "ilst_b19dcf26f22435ae2b8b34cee9b";

    private static McpServerDefinition Parse(string command)
    {
        Result<McpServerDefinition, string> result = McpCommandParser.Parse(command);
        Assert.True(result.IsOk(out McpServerDefinition? definition, out string? error), error);
        return definition;
    }

    private static string ParseError(string command)
    {
        Result<McpServerDefinition, string> result = McpCommandParser.Parse(command);
        Assert.True(result.IsErr(out string? error, out _));
        return error;
    }

    [Fact]
    public void Parse_IllustratorClaudeCodeCommand_ReadsNameUrlAndHeader()
    {
        McpServerDefinition server = Parse(
            $"claude mcp add --transport http --header \"Authorization: Bearer {Token}\" "
            + "--scope user illustrator http://localhost:18412/v1/mcp");

        Assert.Equal("illustrator", server.Name);
        Assert.Equal("http://localhost:18412/v1/mcp", server.Url);
        Assert.True(server.IsRemote);
        Assert.Equal($"Bearer {Token}", server.Headers["Authorization"]);
        Assert.Null(server.Command);

        // --scope selects Claude Code's settings FILE, not an OAuth scope. Carrying it over would
        // ask the authorization server for a scope called "user".
        Assert.Null(server.Scope);
    }

    [Fact]
    public void Parse_IllustratorCodexCommand_TurnsBearerVariableIntoAuthorizationHeader()
    {
        McpServerDefinition server = Parse(
            $"set \"ADOBE_ILLUSTRATOR_MCP_BEARER_TOKEN={Token}\" && codex mcp add adobe-illustrator "
            + "--url http://localhost:18412/v1/mcp "
            + "--bearer-token-env-var ADOBE_ILLUSTRATOR_MCP_BEARER_TOKEN");

        Assert.Equal("adobe-illustrator", server.Name);
        Assert.Equal("http://localhost:18412/v1/mcp", server.Url);

        // The prelude carried the value, so the header gets the token itself: a ${VAR} reference
        // would resolve to nothing until the user set a variable nobody told them about.
        Assert.Equal($"Bearer {Token}", server.Headers["Authorization"]);
    }

    [Fact]
    public void Parse_CodexBearerVariableWithNoPrelude_KeepsTheReference()
    {
        McpServerDefinition server = Parse(
            "codex mcp add illustrator --url http://localhost:18412/v1/mcp "
            + "--bearer-token-env-var ADOBE_ILLUSTRATOR_MCP_BEARER_TOKEN");

        Assert.Equal(
            "Bearer ${ADOBE_ILLUSTRATOR_MCP_BEARER_TOKEN}",
            server.Headers["Authorization"]);
    }

    [Fact]
    public void Parse_ClaudeStdioCommandAfterSeparator_ReadsCommandAndArguments()
    {
        McpServerDefinition server = Parse(
            "claude mcp add filesystem -- npx -y @modelcontextprotocol/server-filesystem C:/refs");

        Assert.Equal("filesystem", server.Name);
        Assert.Equal("npx", server.Command);
        Assert.Equal(
            new[] { "-y", "@modelcontextprotocol/server-filesystem", "C:/refs" },
            server.Arguments);
        Assert.False(server.IsRemote);
    }

    [Fact]
    public void Parse_ClaudeStdioCommandWithoutSeparator_StillReadsCommandAndArguments()
    {
        McpServerDefinition server = Parse(
            "claude mcp add everything npx -y @modelcontextprotocol/server-everything");

        Assert.Equal("everything", server.Name);
        Assert.Equal("npx", server.Command);
        Assert.Equal(new[] { "-y", "@modelcontextprotocol/server-everything" }, server.Arguments);
    }

    [Fact]
    public void Parse_ClaudeEnvFlag_ReadsEnvironmentPair()
    {
        McpServerDefinition server = Parse(
            "claude mcp add github --env GITHUB_PERSONAL_ACCESS_TOKEN=ghp_xyz "
            + "-- npx -y @modelcontextprotocol/server-github");

        Assert.Equal("ghp_xyz", server.Environment["GITHUB_PERSONAL_ACCESS_TOKEN"]);
        Assert.Equal("npx", server.Command);
    }

    [Fact]
    public void Parse_PreludeInFrontOfLocalCommand_BecomesTheEntrysEnvironment()
    {
        McpServerDefinition server = Parse(
            "export API_TOKEN=abc123 && claude mcp add thing -- node server.js");

        Assert.Equal("abc123", server.Environment["API_TOKEN"]);
        Assert.Equal("node", server.Command);
        Assert.Equal(new[] { "server.js" }, server.Arguments);
    }

    [Fact]
    public void Parse_CodexCommandPastedUnderTheOtherOption_StillWorks()
    {
        // The grammar is detected, not declared: the two options on the page choose which example to
        // show, and pasting the wrong one must not be a failure mode.
        McpServerDefinition server = Parse(
            "codex mcp add thing --url https://mcp.example.com/mcp");

        Assert.Equal("thing", server.Name);
        Assert.Equal("https://mcp.example.com/mcp", server.Url);
    }

    [Fact]
    public void Parse_MultiLinePaste_IsReadAsOneCommand()
    {
        McpServerDefinition server = Parse(
            "claude mcp add --transport http \\\n  illustrator http://localhost:18412/v1/mcp");

        Assert.Equal("illustrator", server.Name);
        Assert.Equal("http://localhost:18412/v1/mcp", server.Url);
    }

    [Fact]
    public void Parse_FullPathExecutable_IsStillRecognised()
    {
        McpServerDefinition server = Parse(
            "\"C:/Program Files/nodejs/claude.cmd\" mcp add thing https://mcp.example.com/mcp");

        Assert.Equal("thing", server.Name);
        Assert.Equal("https://mcp.example.com/mcp", server.Url);
    }

    [Fact]
    public void Parse_Blank_ReportsSomethingToPaste()
    {
        Assert.Contains("Paste a command", ParseError("   "));
    }

    [Fact]
    public void Parse_UnrelatedCommand_SaysWhatWasExpected()
    {
        string error = ParseError("npm install --save-dev vitest");
        Assert.Contains("claude mcp add", error);
        Assert.Contains("codex mcp add", error);
    }

    [Fact]
    public void Parse_AddJsonForm_SaysWhereToPasteItInstead()
    {
        string error = ParseError("claude mcp add-json thing '{\"command\":\"npx\"}'");
        Assert.Contains("Paste that JSON", error);
    }

    [Fact]
    public void Parse_NameWithNothingAfterIt_SaysSo()
    {
        Assert.Contains("no URL or command", ParseError("claude mcp add lonely"));
    }

    [Fact]
    public void Parse_CodexEntryWithNeitherUrlNorCommand_SaysSo()
    {
        Assert.Contains("neither a --url nor a command", ParseError("codex mcp add lonely"));
    }

    [Fact]
    public void Parse_HttpTransportWithANonUrlTarget_IsRejected()
    {
        string error = ParseError("claude mcp add thing --transport http notaurl");
        Assert.Contains("not a URL", error);
    }

    [Fact]
    public void Parse_SingleQuotedHeader_IsReadTheSameAsDoubleQuoted()
    {
        McpServerDefinition server = Parse(
            "claude mcp add thing --header 'Authorization: Bearer abc' https://mcp.example.com/mcp");

        Assert.Equal("Bearer abc", server.Headers["Authorization"]);
    }

    [Fact]
    public void Parse_HeaderValueContainingColons_KeepsThemAll()
    {
        // Only the FIRST colon separates a header's name from its value.
        McpServerDefinition server = Parse(
            "claude mcp add thing --header \"X-Trace: a:b:c\" https://mcp.example.com/mcp");

        Assert.Equal("a:b:c", server.Headers["X-Trace"]);
    }
}
