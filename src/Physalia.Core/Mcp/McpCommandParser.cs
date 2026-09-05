// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Physalia.Core.Common;

namespace Physalia.Core.Mcp;

/// <summary>
/// Turns an MCP setup command copied from another client's instructions into an
/// <see cref="McpServerDefinition"/>.
/// </summary>
/// <remarks>
/// <para>Every host that exposes an MCP server publishes its connection details as a ready-made
/// command line for the popular clients — Adobe Illustrator's preferences pane offers one for
/// Claude Code and one for Codex, and so do most hosted servers' READMEs. Retyping those five
/// values into a form is where the mistakes happen: a header's name and value get swapped, or
/// Claude Code's <c>--scope user</c> (which selects a settings FILE) is read as an OAuth scope.
/// Parsing the command removes the transcription step entirely.</para>
/// <para>Two grammars are recognised, and which one arrived is DETECTED rather than declared, so a
/// Codex command pasted under the Claude Code option still works — the caller's choice only picks
/// which example to show. What the parser refuses, it refuses with the reason.</para>
/// <para><b>A literal credential in the command is kept literal.</b> The pasted line carries the
/// token in the clear (Claude Code puts it in <c>--header</c>; Codex's Windows form prefixes a
/// <c>set "VAR=…"</c>), and the whole promise of this path is that pasting it connects. Rewriting
/// it to <c>${VAR}</c> would emit a definition that resolves to nothing until the user sets a
/// variable nobody told them about. The file is gitignored and a preset carries only a server's
/// NAME, so the token travels nowhere; someone who prefers the indirection can edit the entry
/// afterwards and write the reference by hand.</para>
/// </remarks>
public static class McpCommandParser
{
    /// <summary>
    /// Parses one setup command into a server definition.
    /// </summary>
    /// <param name="command">
    /// The command as pasted, optionally spanning several lines and optionally carrying a shell
    /// prelude that sets an environment variable.
    /// </param>
    /// <returns>
    /// The definition the command describes, or an error naming what could not be read.
    /// </returns>
    public static Result<McpServerDefinition, string> Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new Result<McpServerDefinition, string>.Err("Paste a command first.");
        }

        // A pasted line often arrives with a shell prelude — Codex's Windows form is
        // `set "VAR=token" && codex mcp add …`. The assignments are pulled out first, because the
        // token they carry is what a later --bearer-token-env-var refers to.
        (Dictionary<string, string> assignments, string remainder) = LiftAssignments(command);

        List<string> tokens = Tokenize(remainder);
        if (tokens.Count == 0)
        {
            return new Result<McpServerDefinition, string>.Err("Paste a command first.");
        }

        int start = FindSubcommand(tokens, out string client);

        if (start < 0)
        {
            return new Result<McpServerDefinition, string>.Err(
                "That does not look like an MCP setup command. Expected one starting "
                + "\"claude mcp add\" or \"codex mcp add\".");
        }

        if (string.Equals(client, "claude", StringComparison.OrdinalIgnoreCase)
            && tokens.Count > start
            && tokens[start].Equals("add-json", StringComparison.OrdinalIgnoreCase))
        {
            return new Result<McpServerDefinition, string>.Err(
                "This is the add-json form, which carries a whole JSON block rather than flags. "
                + "Paste that JSON into the \"Add from a config\" box, or fill the form in manually.");
        }

        List<string> rest = tokens.Skip(start + 1).ToList();

        return string.Equals(client, "codex", StringComparison.OrdinalIgnoreCase)
            ? ParseCodex(rest, assignments)
            : ParseClaude(rest, assignments);
    }

    // `claude mcp add [flags] <name> <commandOrUrl> [args…]`, plus the `--` form where everything
    // after the separator is the subprocess command line.
    private static Result<McpServerDefinition, string> ParseClaude(
        List<string> args,
        Dictionary<string, string> assignments)
    {
        string? transport = null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        var positional = new List<string>();
        List<string>? afterSeparator = null;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            if (arg == "--")
            {
                afterSeparator = args.Skip(i + 1).ToList();
                break;
            }

            // Once the name and its command-or-url are in hand, flag parsing STOPS: what follows
            // belongs to the launched program, and `claude mcp add x npx -y pkg` would otherwise
            // lose "pkg" to a "-y" this parser has no business reading. Claude Code recommends the
            // "--" separator for exactly this reason, but the bare form is legal and common.
            if (positional.Count >= 2)
            {
                positional.AddRange(args.Skip(i));
                break;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--transport":
                case "-t":
                    transport = Next(args, ref i);
                    break;

                case "--header":
                case "-H":
                    // "Name: Value" — a COLON, unlike --env's equals sign. Getting these two
                    // confused is the single commonest hand-entry mistake this path exists to stop.
                    if (Next(args, ref i) is { } header && SplitHeader(header) is var (name, value))
                    {
                        headers[name] = value;
                    }

                    break;

                case "--env":
                case "-e":
                    if (Next(args, ref i) is { } pair && SplitAssignment(pair) is var (key, val))
                    {
                        env[key] = val;
                    }

                    break;

                // Claude Code's --scope names the SETTINGS FILE the entry is written to (user /
                // project / local). It is emphatically not an OAuth scope, and copying it into one
                // would ask the authorization server for a scope called "user".
                case "--scope":
                case "-s":
                    Next(args, ref i);
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        // An unknown flag may or may not take a value; skipping only the flag keeps
                        // the positional reading intact for the flags that do not.
                        if (arg.Contains('=', StringComparison.Ordinal))
                        {
                            break;
                        }

                        if (i + 1 < args.Count && !args[i + 1].StartsWith('-'))
                        {
                            i++;
                        }

                        break;
                    }

                    positional.Add(arg);
                    break;
            }
        }

        if (positional.Count == 0)
        {
            return new Result<McpServerDefinition, string>.Err(
                "The command names no server. Expected \"claude mcp add <name> <url-or-command>\".");
        }

        string serverName = positional[0];

        if (afterSeparator is { Count: > 0 })
        {
            return new Result<McpServerDefinition, string>.Ok(Local(
                serverName, afterSeparator, env, assignments));
        }

        if (positional.Count < 2)
        {
            return new Result<McpServerDefinition, string>.Err(
                $"'{serverName}' has no URL or command after it.");
        }

        string target = positional[1];

        bool remote = LooksLikeUrl(target)
            || (transport is not null
                && !transport.Equals("stdio", StringComparison.OrdinalIgnoreCase));

        if (remote)
        {
            if (!LooksLikeUrl(target))
            {
                return new Result<McpServerDefinition, string>.Err(
                    $"The transport is '{transport}' but '{target}' is not a URL.");
            }

            return new Result<McpServerDefinition, string>.Ok(Remote(serverName, target, headers, null));
        }

        return new Result<McpServerDefinition, string>.Ok(Local(
            serverName,
            positional.Skip(1).ToList(),
            env,
            assignments));
    }

    // `codex mcp add <name> [--url <url>] [--bearer-token-env-var VAR] [--env K=V] [-- cmd args…]`
    private static Result<McpServerDefinition, string> ParseCodex(
        List<string> args,
        Dictionary<string, string> assignments)
    {
        string? url = null;
        string? bearerVariable = null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        var positional = new List<string>();
        List<string>? afterSeparator = null;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            if (arg == "--")
            {
                afterSeparator = args.Skip(i + 1).ToList();
                break;
            }

            // Same rule as the Claude grammar: past the name and its command, the words are the
            // launched program's own.
            if (positional.Count >= 2)
            {
                positional.AddRange(args.Skip(i));
                break;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--url":
                    url = Next(args, ref i);
                    break;

                case "--bearer-token-env-var":
                    bearerVariable = Next(args, ref i);
                    break;

                case "--header":
                    if (Next(args, ref i) is { } header && SplitHeader(header) is var (name, value))
                    {
                        headers[name] = value;
                    }

                    break;

                case "--env":
                    if (Next(args, ref i) is { } pair && SplitAssignment(pair) is var (key, val))
                    {
                        env[key] = val;
                    }

                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        if (!arg.Contains('=', StringComparison.Ordinal)
                            && i + 1 < args.Count
                            && !args[i + 1].StartsWith('-'))
                        {
                            i++;
                        }

                        break;
                    }

                    positional.Add(arg);
                    break;
            }
        }

        if (positional.Count == 0)
        {
            return new Result<McpServerDefinition, string>.Err(
                "The command names no server. Expected \"codex mcp add <name> --url <url>\".");
        }

        string serverName = positional[0];

        if (bearerVariable is not null && !headers.ContainsKey("Authorization"))
        {
            // The variable is what the command's own `set` prelude assigned, so prefer the value it
            // carried; a bare command with no prelude keeps the reference for the environment to
            // resolve, which is exactly what Codex itself would have done.
            headers["Authorization"] = assignments.TryGetValue(bearerVariable, out string? token)
                ? $"Bearer {token}"
                : $"Bearer ${{{bearerVariable}}}";
        }

        if (url is not null)
        {
            return LooksLikeUrl(url)
                ? new Result<McpServerDefinition, string>.Ok(Remote(serverName, url, headers, null))
                : new Result<McpServerDefinition, string>.Err($"'{url}' is not a URL.");
        }

        if (afterSeparator is { Count: > 0 })
        {
            return new Result<McpServerDefinition, string>.Ok(Local(
                serverName, afterSeparator, env, assignments));
        }

        if (positional.Count > 1)
        {
            return new Result<McpServerDefinition, string>.Ok(Local(
                serverName, positional.Skip(1).ToList(), env, assignments));
        }

        return new Result<McpServerDefinition, string>.Err(
            $"'{serverName}' has neither a --url nor a command to launch.");
    }

    private static McpServerDefinition Remote(
        string name,
        string url,
        Dictionary<string, string> headers,
        string? scope) =>
        new(
            name.Trim(),
            null,
            Array.Empty<string>(),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            url.Trim(),
            headers,
            scope);

    private static McpServerDefinition Local(
        string name,
        List<string> commandLine,
        Dictionary<string, string> env,
        Dictionary<string, string> assignments)
    {
        // A prelude assignment in front of a local command is what Codex's --env would have carried,
        // so it belongs in the entry's own environment rather than being dropped.
        foreach (KeyValuePair<string, string> pair in assignments)
        {
            if (!env.ContainsKey(pair.Key))
            {
                env[pair.Key] = pair.Value;
            }
        }

        return new McpServerDefinition(
            name.Trim(),
            commandLine[0],
            commandLine.Skip(1).ToList(),
            env,
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            null);
    }

    // Finds "mcp add" and reports which client's grammar precedes it. Returns the index of the
    // subcommand word, so the caller skips past it. Anything ahead of the client name — `&&`, a
    // prelude, a full path to the executable — is ignored.
    private static int FindSubcommand(List<string> tokens, out string client)
    {
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (!tokens[i].Equals("mcp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string next = tokens[i + 1];
            if (!next.Equals("add", StringComparison.OrdinalIgnoreCase)
                && !next.Equals("add-json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The executable is the word before "mcp", possibly a path or a .cmd shim.
            string executable = i > 0 ? tokens[i - 1] : string.Empty;
            client = executable.Contains("codex", StringComparison.OrdinalIgnoreCase)
                ? "codex"
                : "claude";

            return i + 1;
        }

        client = string.Empty;
        return -1;
    }

    // Pulls `set "VAR=value"` / `set VAR=value` / `export VAR=value` off the front and returns what
    // is left. Only assignments BEFORE the client invocation are lifted: one appearing later would
    // be an argument, not a prelude.
    private static (Dictionary<string, string> Assignments, string Remainder) LiftAssignments(string command)
    {
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string working = command.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        // Split on shell separators, keeping the order, and stop lifting at the first segment that
        // is not an assignment.
        string[] segments = working.Split(new[] { "&&", "&", ";" }, StringSplitOptions.None);
        var kept = new List<string>();
        bool stillPrelude = true;

        foreach (string segment in segments)
        {
            string trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (stillPrelude && TryReadAssignment(trimmed, out string? name, out string? value))
            {
                assignments[name] = value;
                continue;
            }

            stillPrelude = false;
            kept.Add(trimmed);
        }

        return (assignments, string.Join(" ", kept));
    }

    private static bool TryReadAssignment(string segment, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;

        string body = segment;
        foreach (string keyword in new[] { "set ", "setx ", "export " })
        {
            if (body.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                body = body.Substring(keyword.Length).Trim();
                break;
            }
        }

        if (ReferenceEquals(body, segment) && !body.Contains('=', StringComparison.Ordinal))
        {
            return false;
        }

        body = Unquote(body.Trim());

        int split = body.IndexOf('=', StringComparison.Ordinal);
        if (split <= 0)
        {
            return false;
        }

        string candidate = body.Substring(0, split).Trim();

        // An assignment's name is a bare identifier. Anything else is a flag or a path that merely
        // contains an equals sign.
        if (candidate.Length == 0 || !candidate.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return false;
        }

        name = candidate;
        value = Unquote(body.Substring(split + 1).Trim());
        return true;
    }

    // Splits a command line the way a shell would: quotes group, everything else breaks on spaces.
    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        bool any = false;

        foreach (char c in command)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                any = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (any || current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    any = false;
                }

                continue;
            }

            current.Append(c);
        }

        if (any || current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        // A pasted multi-line command keeps its continuation marks, and an unquoted "\" or "^" is
        // punctuation rather than an argument — left in, it becomes the server's name.
        tokens.RemoveAll(t => t is "\\" or "^");

        return tokens;
    }

    private static string? Next(List<string> args, ref int index) =>
        index + 1 < args.Count ? args[++index] : null;

    private static (string Name, string Value) SplitHeader(string header)
    {
        int split = header.IndexOf(':', StringComparison.Ordinal);
        return split <= 0
            ? (header.Trim(), string.Empty)
            : (header.Substring(0, split).Trim(), header.Substring(split + 1).Trim());
    }

    private static (string Key, string Value) SplitAssignment(string pair)
    {
        int split = pair.IndexOf('=', StringComparison.Ordinal);
        return split <= 0
            ? (pair.Trim(), string.Empty)
            : (pair.Substring(0, split).Trim(), pair.Substring(split + 1).Trim());
    }

    private static bool LooksLikeUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string value) =>
        value.Length >= 2
        && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value.Substring(1, value.Length - 2)
            : value;
}
