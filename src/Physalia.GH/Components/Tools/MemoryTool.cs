// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Memory;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that gives the model a persistent, file-backed memory. It advertises a
/// single provider-agnostic <c>memory</c> tool whose <c>command</c> enum mirrors Anthropic's memory
/// tool (view, create, str_replace, insert, delete, rename) — a well-worn file-editing shape every
/// frontier model handles, so the identical schema works on OpenAI and Gemini too. When the model
/// calls it, the dispatched signal arrives from a Router, the node runs the command against
/// <c>Files/memories</c> (a global folder shared across documents, plus a per-document local folder),
/// and it emits the result as a tool result (wire its Result output through a Feedback component into a
/// Feedback Collector and back to the Router's Results input).
///
/// <para>Memory operations are fast local file I/O, so this tool runs synchronously within the
/// dispatch solve. Pair it with a Memory Grounding wired into the Recorder so the model is told the
/// memory exists and is nudged to consult it — without that grounding the model is never informed of
/// the feature.</para>
/// </summary>
public class MemoryTool : ToolComponentBase
{
    private static readonly ToolDefinition ToolDef = new(
        "memory",
        "Read and write your persistent memory — files that survive across conversations. Commands: "
        + "\"view\" (list a directory or read a file), \"create\" (write/overwrite a file with file_text), "
        + "\"str_replace\" (replace old_str with new_str in a file), \"insert\" (insert insert_text at "
        + "insert_line), \"delete\" (remove a file), \"rename\" (move old_path to new_path). All paths are "
        + "under /memories: use /memories/global/<name>.md for facts shared across every Grasshopper "
        + "document, and /memories/local/<name>.md for facts specific to the current document. View "
        + "/memories at the start of a task to see what you already know.",
        "{\"type\":\"object\",\"properties\":{"
        + "\"command\":{\"type\":\"string\",\"enum\":[\"view\",\"create\",\"str_replace\",\"insert\",\"delete\",\"rename\"],\"description\":\"The memory operation to perform.\"},"
        + "\"path\":{\"type\":\"string\",\"description\":\"Target path under /memories/global or /memories/local (e.g. /memories/global/preferences.md).\"},"
        + "\"file_text\":{\"type\":\"string\",\"description\":\"For create: the full file contents to write.\"},"
        + "\"view_range\":{\"type\":\"array\",\"items\":{\"type\":\"integer\"},\"description\":\"For view of a file: [startLine, endLine] (1-based, inclusive; end -1 = to end).\"},"
        + "\"old_str\":{\"type\":\"string\",\"description\":\"For str_replace: the exact text to replace (must occur exactly once).\"},"
        + "\"new_str\":{\"type\":\"string\",\"description\":\"For str_replace: the replacement text.\"},"
        + "\"insert_line\":{\"type\":\"integer\",\"description\":\"For insert: number of existing lines to keep before the inserted text (0 = top of file).\"},"
        + "\"insert_text\":{\"type\":\"string\",\"description\":\"For insert: the text to insert.\"},"
        + "\"old_path\":{\"type\":\"string\",\"description\":\"For rename: the current path.\"},"
        + "\"new_path\":{\"type\":\"string\",\"description\":\"For rename: the destination path.\"}"
        + "},\"required\":[\"command\"]}");

    private MemoryRoots? _roots;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryTool"/> class.
    /// </summary>
    public MemoryTool()
        : base("Memory", "Memory", "A tool the model calls to read and write its persistent memory (global + per-document).")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7B4D9E12-2C6A-4F58-9A31-5E0C7D8B4A16");

    /// <inheritdoc/>
    protected override ToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    /// <remarks>Resolve the memory roots once per solve so each dispatched call reuses them.</remarks>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        _roots = MemoryLocations.ResolveRoots(OnPingDocument());
    }

    /// <inheritdoc/>
    protected override ToolCallResult ExecuteCall(ToolCallContent call)
    {
        MemoryRoots roots = _roots ?? MemoryLocations.ResolveRoots(OnPingDocument());
        MemoryOutcome outcome = MemoryStore.Execute(call.InputJson, roots);
        return outcome.IsError ? ToolCallResult.Error(outcome.Content) : ToolCallResult.Ok(outcome.Content);
    }
}
