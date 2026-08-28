// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using System.Text.Json;

namespace Physalia.Core.Mcp;

/// <summary>
/// Reads the user's MCP server list from <c>Files/MCP_SERVERS.YAML</c>.
/// </summary>
/// <remarks>
/// <para>The file uses the standard <c>mcpServers</c> block that Claude Code, Claude Desktop and
/// Cursor all use, so a server configuration copied out of any project's README works unchanged.
/// Both the JSON form (the file may be pasted wholesale from one of those clients) and a 2-space
/// YAML form are accepted; a leading <c>{</c> selects the JSON reader.</para>
/// <para>Physalia ships no server definitions of its own — only a commented example. Maintaining a
/// catalog of servers is explicitly not this plug-in's job.</para>
/// <para>Values may reference an environment variable as <c>${NAME}</c>, which is how a token stays
/// out of the file itself. An unset variable is left as written rather than blanked, so the failure
/// shows up as a server rejecting the credential rather than as a silently empty one.</para>
/// </remarks>
public static class McpServerLibrary
{
    /// <summary>
    /// Reads and parses the server list at the given path.
    /// </summary>
    /// <param name="filePath">Absolute path to the configuration file.</param>
    /// <returns>One definition per entry, or an empty list if the file is absent or unreadable.</returns>
    public static IReadOnlyList<McpServerDefinition> Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return Array.Empty<McpServerDefinition>();
        }

        try
        {
            return Parse(File.ReadAllText(filePath));
        }
        catch (IOException)
        {
            return Array.Empty<McpServerDefinition>();
        }
    }

    /// <summary>
    /// Parses configuration content. Pure — no file access.
    /// </summary>
    /// <param name="content">The file's text, in either the JSON or the YAML form.</param>
    /// <returns>One definition per entry, in the order they appear.</returns>
    public static IReadOnlyList<McpServerDefinition> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<McpServerDefinition>();
        }

        return content.TrimStart().StartsWith("{", StringComparison.Ordinal)
            ? ParseJson(content)
            : ParseYaml(content);
    }

    /// <summary>
    /// Expands <c>${NAME}</c> references against the process environment.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The value with every resolvable reference substituted.</returns>
    public static string ExpandEnvironment(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("${", StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        int index = 0;

        while (index < value.Length)
        {
            int open = value.IndexOf("${", index, StringComparison.Ordinal);
            if (open < 0)
            {
                result.Append(value, index, value.Length - index);
                break;
            }

            int close = value.IndexOf('}', open + 2);
            if (close < 0)
            {
                result.Append(value, index, value.Length - index);
                break;
            }

            result.Append(value, index, open - index);
            string name = value.Substring(open + 2, close - open - 2);
            string? resolved = Environment.GetEnvironmentVariable(name);

            // An unset variable keeps its literal form on purpose: a blank would look like a
            // configured-but-empty credential, which is much harder to diagnose.
            result.Append(resolved ?? value.Substring(open, close - open + 1));
            index = close + 1;
        }

        return result.ToString();
    }

    private static IReadOnlyList<McpServerDefinition> ParseJson(string content)
    {
        var servers = new List<McpServerDefinition>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                content,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (!document.RootElement.TryGetProperty("mcpServers", out JsonElement block) ||
                block.ValueKind != JsonValueKind.Object)
            {
                return servers;
            }

            foreach (JsonProperty entry in block.EnumerateObject())
            {
                servers.Add(FromJsonEntry(entry.Name, entry.Value));
            }
        }
        catch (JsonException)
        {
            return Array.Empty<McpServerDefinition>();
        }

        return servers;
    }

    private static McpServerDefinition FromJsonEntry(string name, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return McpServerDefinition.Unrunnable(name);
        }

        var args = new List<string>();
        if (body.TryGetProperty("args", out JsonElement argsElement) &&
            argsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in argsElement.EnumerateArray())
            {
                args.Add(ExpandEnvironment(item.ToString()));
            }
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (body.TryGetProperty("env", out JsonElement envElement) &&
            envElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty pair in envElement.EnumerateObject())
            {
                env[pair.Name] = ExpandEnvironment(pair.Value.ToString());
            }
        }

        return new McpServerDefinition(
            name,
            ReadJsonString(body, "command"),
            args,
            env,
            ReadJsonString(body, "cwd") ?? ReadJsonString(body, "workingDirectory"),
            ReadJsonString(body, "url"));
    }

    private static string? ReadJsonString(JsonElement body, string property) =>
        body.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? ExpandEnvironment(value.GetString() ?? string.Empty)
            : null;

    // Hand-rolled rather than taking a YAML dependency, matching Config/Api.cs. Only the shapes the
    // standard mcpServers block actually uses are supported: nested maps, inline and block
    // sequences, quoted and bare scalars, '#' comments.
    private static IReadOnlyList<McpServerDefinition> ParseYaml(string content)
    {
        var servers = new List<McpServerDefinition>();

        string? name = null;
        string? command = null;
        string? url = null;
        string? workingDirectory = null;
        var args = new List<string>();
        var env = new Dictionary<string, string>(StringComparer.Ordinal);

        // Which multi-line field the following deeper-indented lines belong to.
        string? openField = null;
        int serverIndent = -1;
        bool inBlock = false;

        void Flush()
        {
            if (name is not null)
            {
                servers.Add(new McpServerDefinition(
                    name,
                    command,
                    args.ToList(),
                    new Dictionary<string, string>(env, StringComparer.Ordinal),
                    workingDirectory,
                    url));
            }

            name = null;
            command = null;
            url = null;
            workingDirectory = null;
            args.Clear();
            env.Clear();
            openField = null;
        }

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = StripComment(line);
            if (trimmed.Length == 0)
            {
                continue;
            }

            int indent = line.Length - line.TrimStart().Length;

            if (!inBlock)
            {
                // Tolerate the wrapper being absent, so a fragment pasted on its own still reads.
                if (trimmed.StartsWith("mcpServers:", StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = true;
                }
                else if (indent == 0 && trimmed.EndsWith(":", StringComparison.Ordinal))
                {
                    inBlock = true;
                    serverIndent = 0;
                    name = trimmed.TrimEnd(':').Trim();
                }

                continue;
            }

            if (serverIndent < 0)
            {
                serverIndent = indent;
            }

            // A block-sequence item belongs to whichever list field is currently open.
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-")
            {
                if (openField == "args")
                {
                    args.Add(Unquote(trimmed.Length > 1 ? trimmed.Substring(1).Trim() : string.Empty));
                }

                continue;
            }

            (string key, string value) = SplitPair(trimmed);
            if (key.Length == 0)
            {
                continue;
            }

            if (indent <= serverIndent)
            {
                // A new server entry at the shallowest depth inside the block.
                Flush();
                name = key;
                continue;
            }

            // Deeper than a field means it is a member of the currently open map field.
            if (openField == "env" && indent > FieldIndent(serverIndent))
            {
                env[key] = ExpandEnvironment(Unquote(value));
                continue;
            }

            openField = null;

            switch (key.ToLowerInvariant())
            {
                case "command":
                    command = ExpandEnvironment(Unquote(value));
                    break;
                case "url":
                    url = ExpandEnvironment(Unquote(value));
                    break;
                case "cwd":
                case "workingdirectory":
                    workingDirectory = ExpandEnvironment(Unquote(value));
                    break;
                case "args":
                    if (value.StartsWith("[", StringComparison.Ordinal))
                    {
                        args.AddRange(SplitFlowSequence(value).Select(ExpandEnvironment));
                    }
                    else
                    {
                        openField = "args";
                    }

                    break;
                case "env":
                    openField = "env";
                    break;
                default:
                    // Unknown key (disabled, type, headers…) — ignored rather than rejected, so a
                    // config carrying settings for another host still loads here.
                    break;
            }
        }

        Flush();
        return servers;
    }

    // The indent at which a server's own fields sit, given where its key sits.
    private static int FieldIndent(int serverIndent) => serverIndent + 2;

    private static string StripComment(string line)
    {
        bool inSingle = false;
        bool inDouble = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }
            else if (c == '#' && !inSingle && !inDouble && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line.Substring(0, i).Trim();
            }
        }

        return line.Trim();
    }

    private static (string Key, string Value) SplitPair(string trimmed)
    {
        bool inSingle = false;
        bool inDouble = false;

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }
            else if (c == ':' && !inSingle && !inDouble)
            {
                return (Unquote(trimmed.Substring(0, i).Trim()), trimmed.Substring(i + 1).Trim());
            }
        }

        return (string.Empty, string.Empty);
    }

    private static IEnumerable<string> SplitFlowSequence(string value)
    {
        string inner = value.Trim();
        if (inner.StartsWith("[", StringComparison.Ordinal))
        {
            inner = inner.Substring(1);
        }

        int end = inner.LastIndexOf(']');
        if (end >= 0)
        {
            inner = inner.Substring(0, end);
        }

        var items = new List<string>();
        var current = new StringBuilder();
        bool inSingle = false;
        bool inDouble = false;

        foreach (char c in inner)
        {
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                current.Append(c);
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                current.Append(c);
            }
            else if (c == ',' && !inSingle && !inDouble)
            {
                items.Add(Unquote(current.ToString().Trim()));
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.ToString().Trim().Length > 0)
        {
            items.Add(Unquote(current.ToString().Trim()));
        }

        return items.Where(i => i.Length > 0);
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
