// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Physalia.Core.Mcp;

/// <summary>
/// Edits <c>MCP_SERVERS.YAML</c> in place, one entry at a time, so a server can be configured
/// from the chat window instead of by hand.
/// </summary>
/// <remarks>
/// <para><b>The file is edited, never regenerated.</b> Only the lines of the entry being changed are
/// replaced; every other byte — the shipped commentary, a user's own notes, the ordering, the
/// indentation style — survives untouched. Rewriting the whole file from a parsed model would be far
/// simpler and would silently throw away everything a comment says.</para>
/// <para><b>Values are read unexpanded</b> (<see cref="McpServerLibrary.Parse"/> with
/// <c>expandEnvironment: false</c>). A <c>${TOKEN}</c> exists precisely so the secret is not in the
/// file; populating an editor with the resolved value and saving it back would write that secret
/// into the file on the user's next unrelated edit.</para>
/// <para>The JSON form is read but never written: it is what a pasted
/// <c>claude_desktop_config.json</c> looks like, and re-emitting it as YAML would rewrite a file the
/// user is deliberately sharing with another host. <see cref="DescribeWriteBlock"/> is how the UI
/// knows to show such a file read-only.</para>
/// </remarks>
public static class McpConfigEditor
{
    private const string BlockKey = "mcpServers:";

    /// <summary>
    /// Reports whether content is in the JSON form, which this editor can read but not write.
    /// </summary>
    /// <param name="content">The file's text.</param>
    /// <returns>True when the content is JSON rather than YAML.</returns>
    public static bool IsJsonForm(string content) =>
        !string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("{", StringComparison.Ordinal);

    /// <summary>
    /// Reads the configured servers exactly as written, leaving <c>${NAME}</c> references intact.
    /// </summary>
    /// <param name="content">The file's text.</param>
    /// <returns>One definition per entry, with every value verbatim.</returns>
    public static IReadOnlyList<McpServerDefinition> ParseRaw(string content) =>
        McpServerLibrary.Parse(content, expandEnvironment: false);

    /// <summary>
    /// Reports whether this content can be written by the editor.
    /// </summary>
    /// <param name="content">The file's text.</param>
    /// <returns>Null when the content is editable, otherwise a sentence saying why it is not.</returns>
    public static string? DescribeWriteBlock(string content)
    {
        if (IsJsonForm(content))
        {
            return "This MCP_SERVERS.YAML is in the JSON form. Physalia reads it happily, but saving "
                + "from here would rewrite it as YAML, so edit the file directly instead.";
        }

        // The parser tolerates the mcpServers wrapper being absent; the writer does not, because
        // without it there is no way to tell a server entry from any other top-level key.
        if (FindBlockLine(SplitLines(content)) < 0 && ParseRaw(content).Count > 0)
        {
            return "This MCP_SERVERS.YAML lists servers but has no `mcpServers:` block, so Physalia "
                + "cannot tell which top-level keys are servers. Add the wrapper, or edit the file "
                + "directly.";
        }

        return null;
    }

    /// <summary>
    /// Adds a server entry, or replaces one that is already there.
    /// </summary>
    /// <param name="content">The file's current text.</param>
    /// <param name="entry">The entry to write. Values are written exactly as given.</param>
    /// <param name="replacing">
    /// The name the entry had before, when a rename is being saved, or null for a plain add/update.
    /// </param>
    /// <returns>The new file text.</returns>
    public static string Upsert(string content, McpServerDefinition entry, string? replacing = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            throw new ArgumentException("A server entry needs a name.", nameof(entry));
        }

        List<string> lines = SplitLines(content).ToList();
        int blockLine = FindBlockLine(lines);

        if (blockLine < 0)
        {
            return StartBlock(lines, entry);
        }

        int entryIndent = EntryIndent(lines, blockLine);
        List<EntrySpan> spans = FindEntries(lines, blockLine, entryIndent);

        // A rename retires the old name in place, so the entry keeps its position in the file rather
        // than vanishing from the middle and reappearing at the top.
        EntrySpan existing = spans.FirstOrDefault(s => Matches(s.Name, replacing ?? entry.Name));
        if (existing.Name is null && replacing is not null)
        {
            existing = spans.FirstOrDefault(s => Matches(s.Name, entry.Name));
        }

        List<string> rendered = Render(entry, entryIndent);

        if (existing.Name is not null)
        {
            lines.RemoveRange(existing.Start, existing.End - existing.Start);
            lines.InsertRange(existing.Start, rendered);
        }
        else
        {
            // A new entry goes straight after the block header, which keeps it above the shipped
            // block of commented examples rather than buried beneath them.
            int at = blockLine + 1;
            lines.InsertRange(at, rendered);
            lines.Insert(at + rendered.Count, string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Removes a server entry. Content with no such entry comes back unchanged.
    /// </summary>
    /// <param name="content">The file's current text.</param>
    /// <param name="name">The entry key to remove.</param>
    /// <returns>The new file text.</returns>
    public static string Remove(string content, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return content;
        }

        List<string> lines = SplitLines(content).ToList();
        int blockLine = FindBlockLine(lines);
        if (blockLine < 0)
        {
            return content;
        }

        List<EntrySpan> spans = FindEntries(lines, blockLine, EntryIndent(lines, blockLine));
        EntrySpan match = spans.FirstOrDefault(s => Matches(s.Name, name));
        if (match.Name is null)
        {
            return content;
        }

        lines.RemoveRange(match.Start, match.End - match.Start);

        // Leave at most one blank line where the entry stood.
        if (match.Start > 0 && match.Start < lines.Count &&
            lines[match.Start].Trim().Length == 0 && lines[match.Start - 1].Trim().Length == 0)
        {
            lines.RemoveAt(match.Start);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// One server entry's line range, <c>[Start, End)</c>. <c>End</c> has already backed off any
    /// trailing blank or comment lines, so a comment introducing the NEXT entry is never swallowed
    /// by this one — deleting a server must not take the description of the one below it.
    /// </summary>
    /// <param name="Name">The entry key.</param>
    /// <param name="Start">Index of the entry's own key line.</param>
    /// <param name="End">Index one past the entry's last field line.</param>
    private readonly record struct EntrySpan(string? Name, int Start, int End);

    private static bool Matches(string? a, string? b) =>
        a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string[] SplitLines(string content) =>
        (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    // Starts an mcpServers block in a file that has none, keeping whatever the file already said.
    private static string StartBlock(List<string> lines, McpServerDefinition entry)
    {
        var fresh = new List<string>();

        if (lines.Any(l => l.Trim().Length > 0))
        {
            fresh.AddRange(lines);
            while (fresh.Count > 0 && fresh[^1].Trim().Length == 0)
            {
                fresh.RemoveAt(fresh.Count - 1);
            }

            fresh.Add(string.Empty);
        }

        fresh.Add(BlockKey);
        fresh.Add(string.Empty);
        fresh.AddRange(Render(entry, 2));

        return string.Join(Environment.NewLine, fresh) + Environment.NewLine;
    }

    private static int FindBlockLine(IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith(BlockKey, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // The indent the entries under the block sit at: taken from the first real entry so an edit
    // matches the file's own style, defaulting to the block's indent + 2 for an empty block.
    private static int EntryIndent(IReadOnlyList<string> lines, int blockLine)
    {
        int blockIndent = Indent(lines[blockLine]);

        for (int i = blockLine + 1; i < lines.Count; i++)
        {
            if (IsSkippable(lines[i]))
            {
                continue;
            }

            int indent = Indent(lines[i]);
            return indent > blockIndent ? indent : blockIndent + 2;
        }

        return blockIndent + 2;
    }

    private static List<EntrySpan> FindEntries(IReadOnlyList<string> lines, int blockLine, int entryIndent)
    {
        var spans = new List<EntrySpan>();
        string? openName = null;
        int openStart = 0;

        void Close(int end)
        {
            if (openName is null)
            {
                return;
            }

            int stop = end;
            while (stop > openStart + 1 && IsSkippable(lines[stop - 1]))
            {
                stop--;
            }

            spans.Add(new EntrySpan(openName, openStart, stop));
            openName = null;
        }

        for (int i = blockLine + 1; i < lines.Count; i++)
        {
            if (IsSkippable(lines[i]))
            {
                continue;
            }

            int indent = Indent(lines[i]);

            if (indent < entryIndent)
            {
                Close(i); // dedented out of the block entirely
                return spans;
            }

            if (indent > entryIndent)
            {
                continue; // a field of the entry currently open
            }

            Close(i);

            string key = KeyOf(lines[i]);
            if (key.Length > 0)
            {
                openName = key;
                openStart = i;
            }
        }

        Close(lines.Count);
        return spans;
    }

    private static bool IsSkippable(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal);
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    private static string KeyOf(string line)
    {
        string trimmed = line.Trim();
        int colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 ? trimmed.Substring(0, colon).Trim().Trim('"', '\'') : string.Empty;
    }

    private static List<string> Render(McpServerDefinition entry, int indent)
    {
        string pad = new(' ', indent);
        string field = new(' ', indent + 2);
        string member = new(' ', indent + 4);

        var lines = new List<string> { $"{pad}{Scalar(entry.Name)}:" };

        if (entry.IsRemote)
        {
            lines.Add($"{field}url: {Scalar(entry.Url!)}");

            if (!string.IsNullOrWhiteSpace(entry.Scope))
            {
                lines.Add($"{field}scope: {Scalar(entry.Scope)}");
            }

            if (entry.Headers.Count > 0)
            {
                lines.Add($"{field}headers:");
                foreach (KeyValuePair<string, string> pair in entry.Headers)
                {
                    lines.Add($"{member}{Scalar(pair.Key)}: {Scalar(pair.Value)}");
                }
            }

            return lines;
        }

        lines.Add($"{field}command: {Scalar(entry.Command ?? string.Empty)}");

        if (entry.Arguments.Count > 0)
        {
            // Block form rather than a flow sequence: an argument is often a long path, and one per
            // line is what a human can read and diff.
            lines.Add($"{field}args:");
            foreach (string argument in entry.Arguments)
            {
                lines.Add($"{member}- {Scalar(argument)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.WorkingDirectory))
        {
            lines.Add($"{field}cwd: {Scalar(entry.WorkingDirectory)}");
        }

        if (entry.Environment.Count > 0)
        {
            lines.Add($"{field}env:");
            foreach (KeyValuePair<string, string> pair in entry.Environment)
            {
                lines.Add($"{member}{Scalar(pair.Key)}: {Scalar(pair.Value)}");
            }
        }

        return lines;
    }

    /// <summary>
    /// Quotes a value only where a bare scalar would read back as something else.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The value, quoted and escaped if it needs to be.</returns>
    /// <remarks>
    /// Calibrated to the reader that will consume it — <see cref="McpServerLibrary"/>'s own
    /// hand-rolled YAML, not a general parser. That reader strips a <c>#</c> preceded by whitespace
    /// as a comment, splits a pair on the first unquoted <c>:</c> (so a colon INSIDE a value is
    /// safe and a URL needs no quotes), and reads a leading <c>[</c> on an args value as a flow
    /// sequence.
    /// </remarks>
    private static string Scalar(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        bool needsQuotes =
            value.Contains(" #", StringComparison.Ordinal) ||
            value != value.Trim() ||
            "[]{}\"'&*!|>%@`,#".Contains(value[0], StringComparison.Ordinal) ||
            (value[0] == '-' && value.Length > 1 && value[1] == ' ');

        if (!needsQuotes)
        {
            return value;
        }

        var quoted = new StringBuilder("\"");
        foreach (char c in value)
        {
            if (c is '"' or '\\')
            {
                quoted.Append('\\');
            }

            quoted.Append(c);
        }

        return quoted.Append('"').ToString();
    }
}
