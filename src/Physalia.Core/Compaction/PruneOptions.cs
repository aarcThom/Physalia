// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Compaction;

/// <summary>
/// Selects which parts of a conversation <see cref="ConversationCompactor.Prune"/> removes
/// or shortens. Pruning is content-aware (it edits inside turns) rather than turn-aware, so
/// it targets the bulky, low-value parts of a history — verbose tool output, images, retried
/// feedback — while keeping the conversational thread intact. All options default to off
/// (a no-op), so a caller opts in only to what it wants dropped.
/// </summary>
public sealed record PruneOptions
{
    /// <summary>
    /// Gets a value indicating whether image content blocks are removed. Images dominate token
    /// (and byte) cost; dropping stale ones from history while keeping the surrounding text is a
    /// cheap, large saving.
    /// </summary>
    public bool DropImages { get; init; }

    /// <summary>
    /// Gets a value indicating whether tool exchanges are removed — both the assistant's
    /// <c>tool_use</c> request blocks and the matching <c>tool_result</c> blocks. Use when the
    /// tool traffic is no longer relevant to the model's next step but the prose around it is.
    /// </summary>
    public bool DropToolExchanges { get; init; }

    /// <summary>
    /// Gets a value indicating whether auto-generated feedback turns (validation errors routed
    /// back for correction, marked <see cref="ConvoInstruct.ConversationMessage.IsFeedback"/>)
    /// are removed once the model has moved past them.
    /// </summary>
    public bool DropFeedbackTurns { get; init; }

    /// <summary>
    /// Gets the maximum character length kept for a single tool result; longer results are
    /// truncated with a marker. Null leaves tool results untouched. Tool output is the most
    /// common source of context bloat, so truncating it is often enough on its own.
    /// </summary>
    public int? MaxToolResultChars { get; init; }

    /// <summary>
    /// Gets the maximum character length kept for a single text block; longer text is truncated
    /// with a marker. Null leaves text untouched. Use sparingly — truncating prose loses meaning
    /// more readily than truncating tool dumps.
    /// </summary>
    public int? MaxTextChars { get; init; }

    /// <summary>
    /// Gets how many trailing messages keep their submitted document verbatim; in assistant turns
    /// older than that, a trailing GhJSON/ghpatch document is replaced by a one-line stub. Null
    /// leaves every document intact.
    ///
    /// <para>This is the single largest reclaimable span in a build loop. The model's old documents
    /// are redundant with the canvas-state grounding, which already shows what actually landed —
    /// keeping both means paying twice for the same information, once in a stale and misleading
    /// form.</para>
    /// </summary>
    public int? StaleDocumentKeepLast { get; init; }

    /// <summary>
    /// Gets how many trailing messages keep their plan block; in assistant turns older than that,
    /// the <c>&lt;plan&gt;…&lt;/plan&gt;</c> block is removed. Null leaves every plan block intact.
    ///
    /// <para>The model restates its full plan in every response, so an N-turn window carries N
    /// near-identical copies. Only the most recent one describes the current state, and the Build
    /// Plan tracker reads that one back authoritatively in its progress digest.</para>
    /// </summary>
    public int? StalePlanBlockKeepLast { get; init; }
}
