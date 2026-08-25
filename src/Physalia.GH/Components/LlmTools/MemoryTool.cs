// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Memory;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that gives the model a persistent, file-backed memory. It advertises a
/// single provider-agnostic <c>memory</c> tool whose <c>command</c> enum mirrors Anthropic's memory
/// tool (view, create, str_replace, insert, delete, rename) — a well-worn file-editing shape every
/// frontier model handles, so the identical schema works on OpenAI and Gemini too. When the model
/// calls it, the dispatched signal arrives from a Router, the node runs the command against
/// <c>Files/MEMORIES</c> (a GLOBAL folder shared by every pipeline, plus a LOCAL folder belonging to
/// the harness this node lives in), and it emits the result as a tool result (wire its Result output
/// through a Feedback component into a Feedback Collector and back to the Router's Results input).
///
/// <para><b>Local memory is scoped to the HARNESS, by name</b> (see <see cref="MemoryLocations"/>), not
/// to the Grasshopper file — so the notes travel with the pipeline into another document or out as a
/// preset, renaming the harness starts a fresh set, and two harnesses sharing a name share their
/// notes.</para>
///
/// <para>Memory operations are fast local file I/O, so this tool runs synchronously within the
/// dispatch solve. It carries a <see cref="GroundingDirective"/>, so a Tools Present grounder wired
/// into the Conversation Log tells the model the memory exists and requires it to read it before
/// answering — without that grounder the model is only handed the tool and may never look.</para>
/// </summary>
public class MemoryTool : LlmToolComponentBase
{
    private static readonly LlmToolDefinition ToolDef = new(
        "memory",
        "Read and write your persistent memory — files that survive across conversations. Commands: "
        + "\"view\" (list a directory or read a file), \"create\" (write/overwrite a file with file_text), "
        + "\"str_replace\" (replace old_str with new_str in a file), \"insert\" (insert insert_text at "
        + "insert_line), \"delete\" (remove a file), \"rename\" (move old_path to new_path). All paths are "
        + "under /memories: use /memories/global/<name>.md for facts worth carrying into every pipeline, "
        + "and /memories/local/<name>.md for facts belonging to this harness — the line of work you are "
        + "in — which follow it wherever it is used. View /memories at the start of a task to see what "
        + "you already know.",
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
        : base("Memory", "Memory", "Gives the model somewhere to keep notes between sessions: one set shared by every pipeline, one belonging to this harness and travelling with it. Files live under Files/MEMORIES.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7B4D9E12-2C6A-4F58-9A31-5E0C7D8B4A16");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "A note the model wants to read, write or delete, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises the notebook to the model: list, read, write or delete, in either the shared scope or this harness's own. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire — and it is that grounder that tells the model reading its memory is mandatory.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "What the note said, or confirmation that it was saved, heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    /// <remarks>
    /// A memory the model never opens is worse than no memory: it answers from nothing while the
    /// notes that would have corrected it sit unread, and the user has no way to tell. Advertising
    /// the tool is not enough on its own — the model decides for itself whether a call is warranted,
    /// and on a request that looks self-contained it usually decides not to. So the directive makes
    /// the first read mandatory rather than advisable, and closes the loop by requiring the write.
    /// </remarks>
    public override string GroundingDirective =>
        "MEMORY IS NOT OPTIONAL. Before you answer the first request of a conversation you MUST call "
        + "the \"memory\" tool with command \"view\" on /memories/global and on /memories/local, and "
        + "read whatever you find there. Do this even when the request looks self-contained and even "
        + "when you believe you need nothing — you cannot know what is stored until you look. What you "
        + "read there is established fact about this work and overrides your own assumptions; never "
        + "say you have no record of earlier work without having looked. When a turn establishes "
        + "something durable — a decision, a preference, a constraint, a correction the user made — "
        + "write it to memory before you finish that turn: global for what holds anywhere, local for "
        + "what belongs to this pipeline.";

    /// <inheritdoc/>
    /// <remarks>Resolve the memory roots once per solve so each dispatched call reuses them.</remarks>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        _roots = MemoryLocations.ResolveRoots(PhyDocuments.Harness(this));
    }

    /// <inheritdoc/>
    protected override ToolCallResult ExecuteCall(ToolCallContent call)
    {
        MemoryRoots roots = _roots ?? MemoryLocations.ResolveRoots(PhyDocuments.Harness(this));
        MemoryOutcome outcome = MemoryStore.Execute(call.InputJson, roots);
        return outcome.IsError ? ToolCallResult.Error(outcome.Content) : ToolCallResult.Ok(outcome.Content);
    }
}
