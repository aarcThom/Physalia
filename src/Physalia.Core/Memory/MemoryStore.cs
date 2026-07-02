// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Physalia.Core.Memory;

/// <summary>
/// The provider-agnostic execution backend behind the model-invoked <c>memory</c> tool. It maps the
/// filesystem-style commands the model emits — <c>view</c>, <c>create</c>, <c>str_replace</c>,
/// <c>insert</c>, <c>delete</c>, <c>rename</c> — onto real files under two roots (a global memory
/// shared across all documents, and a per-document local memory). This is deliberately the same
/// command vocabulary Anthropic's memory tool uses, but nothing here is Claude-specific: the caller
/// hands it the raw tool-call arguments as JSON, so the identical layer serves the OpenAI and Gemini
/// tool-calling shapes once their <c>{name, input}</c> envelope is normalised.
///
/// <para>Every path the model supplies is virtual and rooted at <c>/memories</c>. Its first segment
/// selects the scope — <c>/memories/global/…</c> maps under <see cref="MemoryRoots.GlobalDir"/> and
/// <c>/memories/local/…</c> under <see cref="MemoryRoots.LocalDir"/> — and the remainder is resolved
/// beneath that root with <c>..</c>-escape rejected, so a model can never read or write outside its
/// two memory directories.</para>
/// </summary>
public static class MemoryStore
{
    private const string VirtualRoot = "/memories";
    private const string GlobalScope = "global";
    private const string LocalScope = "local";

    // Bound a directory listing so an accidentally huge memory tree can never flood the context.
    private const int MaxListedEntries = 500;

    /// <summary>
    /// Executes one memory command, described by the tool call's raw JSON arguments, against the
    /// supplied roots.
    /// </summary>
    /// <param name="inputJson">
    /// The tool call's argument object as JSON — at minimum a <c>command</c> string, plus the
    /// per-command fields (<c>path</c>, <c>file_text</c>, <c>old_str</c>, <c>new_str</c>,
    /// <c>insert_line</c>, <c>insert_text</c>, <c>view_range</c>, <c>old_path</c>, <c>new_path</c>).
    /// </param>
    /// <param name="roots">The resolved global and local memory directories.</param>
    /// <returns>The result body to return to the model, and whether it represents an error.</returns>
    public static MemoryOutcome Execute(string inputJson, MemoryRoots roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        JsonElement root;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            // Clone so the element outlives the JsonDocument's using scope.
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return MemoryOutcome.Error("Could not parse the tool arguments as JSON.");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return MemoryOutcome.Error("The tool arguments must be a JSON object.");
        }

        string command = GetString(root, "command").Trim().ToLowerInvariant();
        if (command.Length == 0)
        {
            return MemoryOutcome.Error("No 'command' was provided. Use one of: view, create, str_replace, insert, delete, rename.");
        }

        try
        {
            return command switch
            {
                "view" => View(root, roots),
                "create" => Create(root, roots),
                "str_replace" => StrReplace(root, roots),
                "insert" => Insert(root, roots),
                "delete" => Delete(root, roots),
                "rename" => Rename(root, roots),
                _ => MemoryOutcome.Error($"Unknown command \"{command}\". Use one of: view, create, str_replace, insert, delete, rename."),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return MemoryOutcome.Error($"Memory operation failed: {ex.Message}");
        }
    }

    // ---- commands --------------------------------------------------------------------------------

    private static MemoryOutcome View(JsonElement root, MemoryRoots roots)
    {
        string path = GetString(root, "path");
        if (!TryResolve(path, roots, out ResolvedPath resolved, out string error))
        {
            return MemoryOutcome.Error(error);
        }

        // The virtual root lists the two scopes so the model can discover where memory lives.
        if (resolved.Scope == MemoryScope.Root)
        {
            return MemoryOutcome.Ok(
                "Directory: /memories\n"
                + $"- global/   (shared across every Grasshopper document — {CountFiles(roots.GlobalDir)} file(s))\n"
                + $"- local/    (specific to the current document — {CountFiles(roots.LocalDir)} file(s))");
        }

        if (Directory.Exists(resolved.PhysicalPath))
        {
            return MemoryOutcome.Ok(ListDirectory(resolved));
        }

        if (File.Exists(resolved.PhysicalPath))
        {
            (int start, int end) = ReadViewRange(root);
            return MemoryOutcome.Ok(ReadFileForView(resolved, start, end));
        }

        // A scope root that has never been written to is simply empty, not an error.
        if (resolved.Relative.Length == 0)
        {
            return MemoryOutcome.Ok($"Directory {resolved.VirtualPath} is empty.");
        }

        return MemoryOutcome.Error($"No such file or directory: {resolved.VirtualPath}");
    }

    private static MemoryOutcome Create(JsonElement root, MemoryRoots roots)
    {
        if (!TryResolveFile(root, roots, "path", out ResolvedPath resolved, out string error))
        {
            return MemoryOutcome.Error(error);
        }

        string text = GetString(root, "file_text");
        Directory.CreateDirectory(Path.GetDirectoryName(resolved.PhysicalPath)!);
        File.WriteAllText(resolved.PhysicalPath, text);
        return MemoryOutcome.Ok($"Wrote {LineCount(text)} line(s) to {resolved.VirtualPath}.");
    }

    private static MemoryOutcome StrReplace(JsonElement root, MemoryRoots roots)
    {
        if (!TryResolveFile(root, roots, "path", out ResolvedPath resolved, out string error))
        {
            return MemoryOutcome.Error(error);
        }

        if (!File.Exists(resolved.PhysicalPath))
        {
            return MemoryOutcome.Error($"No such file: {resolved.VirtualPath}");
        }

        string oldStr = GetString(root, "old_str");
        if (oldStr.Length == 0)
        {
            return MemoryOutcome.Error("str_replace requires a non-empty 'old_str'.");
        }

        string newStr = GetString(root, "new_str");
        string content = File.ReadAllText(resolved.PhysicalPath);

        int occurrences = CountOccurrences(content, oldStr);
        if (occurrences == 0)
        {
            return MemoryOutcome.Error($"'old_str' was not found in {resolved.VirtualPath}. It must match exactly, including whitespace.");
        }

        if (occurrences > 1)
        {
            return MemoryOutcome.Error($"'old_str' matched {occurrences} times in {resolved.VirtualPath}; it must match exactly once. Add surrounding context to make it unique.");
        }

        int index = content.IndexOf(oldStr, StringComparison.Ordinal);
        string updated = content.Substring(0, index) + newStr + content.Substring(index + oldStr.Length);
        File.WriteAllText(resolved.PhysicalPath, updated);
        return MemoryOutcome.Ok($"Replaced 1 occurrence in {resolved.VirtualPath}.");
    }

    private static MemoryOutcome Insert(JsonElement root, MemoryRoots roots)
    {
        if (!TryResolveFile(root, roots, "path", out ResolvedPath resolved, out string error))
        {
            return MemoryOutcome.Error(error);
        }

        if (!File.Exists(resolved.PhysicalPath))
        {
            return MemoryOutcome.Error($"No such file: {resolved.VirtualPath}");
        }

        if (!TryGetInt(root, "insert_line", out int insertLine))
        {
            return MemoryOutcome.Error("insert requires an integer 'insert_line' (0 inserts before the first line).");
        }

        string insertText = GetString(root, "insert_text");
        var lines = File.ReadAllLines(resolved.PhysicalPath).ToList();
        if (insertLine < 0 || insertLine > lines.Count)
        {
            return MemoryOutcome.Error($"insert_line {insertLine} is out of range; {resolved.VirtualPath} has {lines.Count} line(s) (valid range 0–{lines.Count}).");
        }

        // insert_line is the number of lines to keep before the inserted text (0 = at the very top).
        lines.InsertRange(insertLine, insertText.Replace("\r\n", "\n").Split('\n'));
        File.WriteAllText(resolved.PhysicalPath, string.Join("\n", lines));
        return MemoryOutcome.Ok($"Inserted text after line {insertLine} in {resolved.VirtualPath}.");
    }

    private static MemoryOutcome Delete(JsonElement root, MemoryRoots roots)
    {
        string path = GetString(root, "path");
        if (!TryResolve(path, roots, out ResolvedPath resolved, out string error))
        {
            return MemoryOutcome.Error(error);
        }

        if (resolved.Scope == MemoryScope.Root || resolved.Relative.Length == 0)
        {
            return MemoryOutcome.Error("Refusing to delete a memory scope root. Delete individual files instead.");
        }

        if (File.Exists(resolved.PhysicalPath))
        {
            File.Delete(resolved.PhysicalPath);
            return MemoryOutcome.Ok($"Deleted {resolved.VirtualPath}.");
        }

        if (Directory.Exists(resolved.PhysicalPath))
        {
            Directory.Delete(resolved.PhysicalPath, recursive: true);
            return MemoryOutcome.Ok($"Deleted directory {resolved.VirtualPath} and its contents.");
        }

        return MemoryOutcome.Error($"No such file or directory: {resolved.VirtualPath}");
    }

    private static MemoryOutcome Rename(JsonElement root, MemoryRoots roots)
    {
        // Accept Anthropic's old_path/new_path, and fall back to path/new_path for leniency.
        string oldRaw = GetString(root, "old_path");
        if (oldRaw.Length == 0)
        {
            oldRaw = GetString(root, "path");
        }

        if (!TryResolve(oldRaw, roots, out ResolvedPath from, out string fromError))
        {
            return MemoryOutcome.Error(fromError);
        }

        if (from.Scope == MemoryScope.Root || from.Relative.Length == 0)
        {
            return MemoryOutcome.Error("Provide the file (or directory) to rename, not a memory scope root.");
        }

        if (!TryResolve(GetString(root, "new_path"), roots, out ResolvedPath to, out string toError))
        {
            return MemoryOutcome.Error(toError);
        }

        if (to.Scope == MemoryScope.Root || to.Relative.Length == 0)
        {
            return MemoryOutcome.Error("The destination 'new_path' must be a file or directory under /memories/global or /memories/local.");
        }

        bool isFile = File.Exists(from.PhysicalPath);
        bool isDir = Directory.Exists(from.PhysicalPath);
        if (!isFile && !isDir)
        {
            return MemoryOutcome.Error($"No such file or directory: {from.VirtualPath}");
        }

        if (File.Exists(to.PhysicalPath) || Directory.Exists(to.PhysicalPath))
        {
            return MemoryOutcome.Error($"Destination already exists: {to.VirtualPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(to.PhysicalPath)!);
        if (isFile)
        {
            File.Move(from.PhysicalPath, to.PhysicalPath);
        }
        else
        {
            Directory.Move(from.PhysicalPath, to.PhysicalPath);
        }

        return MemoryOutcome.Ok($"Renamed {from.VirtualPath} to {to.VirtualPath}.");
    }

    // ---- path resolution -------------------------------------------------------------------------

    // Resolves a virtual /memories path to a physical path under the correct scope root, rejecting any
    // path that escapes its root. Returns false with a model-facing error message on a bad path.
    private static bool TryResolve(string path, MemoryRoots roots, out ResolvedPath resolved, out string error)
    {
        resolved = default!;
        error = string.Empty;

        string normalized = (path ?? string.Empty).Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(2);
        }

        string trimmed = normalized.TrimStart('/');

        // Strip an optional leading "memories" segment so both "/memories/global/x" and "global/x" work.
        if (trimmed.Equals("memories", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = string.Empty;
        }
        else if (trimmed.StartsWith("memories/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring("memories/".Length);
        }

        var segments = trimmed
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (segments.Count == 0)
        {
            resolved = new ResolvedPath(MemoryScope.Root, string.Empty, string.Empty, VirtualRoot);
            return true;
        }

        MemoryScope scope;
        string scopeRoot;
        string scopeName = segments[0].ToLowerInvariant();
        switch (scopeName)
        {
            case GlobalScope:
                scope = MemoryScope.Global;
                scopeRoot = roots.GlobalDir;
                break;
            case LocalScope:
                scope = MemoryScope.Local;
                scopeRoot = roots.LocalDir;
                break;
            default:
                error = $"Memory paths must be under {VirtualRoot}/{GlobalScope} or {VirtualRoot}/{LocalScope}. Got: {path}";
                return false;
        }

        var relSegments = segments.Skip(1).ToList();
        if (relSegments.Any(s => s == ".."))
        {
            error = "Memory paths may not contain \"..\".";
            return false;
        }

        string relative = string.Join("/", relSegments);
        string physical = relSegments.Count == 0
            ? scopeRoot
            : Path.Combine(scopeRoot, Path.Combine(relSegments.ToArray()));

        // Defence in depth: the resolved absolute path must stay within the scope root.
        string fullRoot = Path.GetFullPath(scopeRoot);
        string fullPhysical = Path.GetFullPath(physical);
        if (!IsWithin(fullRoot, fullPhysical))
        {
            error = "Resolved path escapes the memory directory.";
            return false;
        }

        string virtualPath = $"{VirtualRoot}/{scopeName}" + (relative.Length > 0 ? "/" + relative : string.Empty);
        resolved = new ResolvedPath(scope, relative, fullPhysical, virtualPath);
        return true;
    }

    // Resolves a path that must denote a concrete file (rejects the root and scope roots).
    private static bool TryResolveFile(JsonElement root, MemoryRoots roots, string field, out ResolvedPath resolved, out string error)
    {
        if (!TryResolve(GetString(root, field), roots, out resolved, out error))
        {
            return false;
        }

        if (resolved.Scope == MemoryScope.Root || resolved.Relative.Length == 0)
        {
            error = $"'{field}' must be a file under {VirtualRoot}/{GlobalScope} or {VirtualRoot}/{LocalScope} (for example {VirtualRoot}/{GlobalScope}/notes.md).";
            return false;
        }

        return true;
    }

    private static bool IsWithin(string root, string candidate)
    {
        string a = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.Equals(a, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // ---- listing / reading -----------------------------------------------------------------------

    private static string ListDirectory(ResolvedPath resolved)
    {
        var entries = new List<string>();

        foreach (string dir in Directory.EnumerateDirectories(resolved.PhysicalPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(Path.GetFileName(dir) + "/");
        }

        foreach (string file in Directory.EnumerateFiles(resolved.PhysicalPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(Path.GetFileName(file));
        }

        if (entries.Count == 0)
        {
            return $"Directory {resolved.VirtualPath} is empty.";
        }

        bool truncated = entries.Count > MaxListedEntries;
        IEnumerable<string> shown = truncated ? entries.Take(MaxListedEntries) : entries;

        var sb = new StringBuilder();
        sb.Append("Directory ").Append(resolved.VirtualPath).Append(':').Append('\n');
        sb.Append(string.Join("\n", shown.Select(e => "- " + e)));
        if (truncated)
        {
            sb.Append($"\n… and {entries.Count - MaxListedEntries} more.");
        }

        return sb.ToString();
    }

    private static string ReadFileForView(ResolvedPath resolved, int start, int end)
    {
        string[] lines = File.ReadAllLines(resolved.PhysicalPath);
        if (lines.Length == 0)
        {
            return $"{resolved.VirtualPath} is empty.";
        }

        // view_range is 1-based inclusive; an end of -1 means "to the end of the file".
        int from = start <= 0 ? 1 : start;
        int to = end < 0 || end > lines.Length ? lines.Length : end;
        if (from > lines.Length)
        {
            return $"{resolved.VirtualPath} has {lines.Length} line(s); requested start {from} is past the end.";
        }

        var sb = new StringBuilder();
        sb.Append(resolved.VirtualPath).Append(":\n");
        int width = to.ToString().Length;
        for (int i = from; i <= to; i++)
        {
            sb.Append(i.ToString().PadLeft(width)).Append('\t').Append(lines[i - 1]).Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static (int Start, int End) ReadViewRange(JsonElement root)
    {
        if (root.TryGetProperty("view_range", out JsonElement range)
            && range.ValueKind == JsonValueKind.Array)
        {
            var values = range.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _))
                .Select(e => e.GetInt32())
                .ToList();
            if (values.Count >= 2)
            {
                return (values[0], values[1]);
            }
        }

        return (0, -1);
    }

    private static int CountFiles(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0;

    // ---- json / string helpers -------------------------------------------------------------------

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = 0;
        return element.TryGetProperty(property, out JsonElement e)
            && e.ValueKind == JsonValueKind.Number
            && e.TryGetInt32(out value);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static int LineCount(string text) =>
        text.Length == 0 ? 0 : text.Replace("\r\n", "\n").Split('\n').Length;

    private enum MemoryScope
    {
        Root,
        Global,
        Local,
    }

    private sealed record ResolvedPath(MemoryScope Scope, string Relative, string PhysicalPath, string VirtualPath);
}

/// <summary>
/// The two physical directories the memory tool operates over: a global memory shared across every
/// Grasshopper document, and a local memory specific to the current document.
/// </summary>
/// <param name="GlobalDir">Absolute path of the global (shared) memory directory.</param>
/// <param name="LocalDir">Absolute path of the current document's local memory directory.</param>
public sealed record MemoryRoots(string GlobalDir, string LocalDir);

/// <summary>
/// The outcome of a memory operation: the body returned to the model and whether it is an error
/// (mapped to the tool result's <c>is_error</c> flag so the model can self-correct).
/// </summary>
/// <param name="Content">The result body returned to the model.</param>
/// <param name="IsError">True when the operation failed.</param>
public sealed record MemoryOutcome(string Content, bool IsError)
{
    /// <summary>
    /// Creates a successful outcome.
    /// </summary>
    /// <param name="content">The result body.</param>
    /// <returns>A success <see cref="MemoryOutcome"/>.</returns>
    public static MemoryOutcome Ok(string content) => new(content, false);

    /// <summary>
    /// Creates an error outcome.
    /// </summary>
    /// <param name="content">The error body returned to the model.</param>
    /// <returns>An error <see cref="MemoryOutcome"/>.</returns>
    public static MemoryOutcome Error(string content) => new(content, true);
}
