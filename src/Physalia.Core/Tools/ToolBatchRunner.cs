// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tools;

/// <summary>
/// The outcome of a single tool call: the result body and whether it represents an error.
/// </summary>
/// <param name="Content">The result body returned to the model.</param>
/// <param name="IsError">True when the call failed.</param>
public readonly record struct ToolCallOutcome(string Content, bool IsError);

/// <summary>
/// The assembled result of running a dispatched batch: one tool_result block per call plus the
/// newline-joined trace payload.
/// </summary>
/// <param name="Blocks">One <see cref="ToolResultContent"/> per call, echoing each call's id.</param>
/// <param name="Payload">The newline-joined result text (signal trace payload).</param>
public sealed record ToolBatchResult(IReadOnlyList<MessageContent> Blocks, string Payload);

/// <summary>
/// Pure policy enforcing the tool-node batch contract: a single dispatched signal can carry several
/// tool calls (parallel tool use), and the provider requires <b>one</b> tool_result per tool_use id.
/// This runs each call through a host-supplied executor and produces exactly one
/// <see cref="ToolResultContent"/> per call. It owns no async lifecycle — the host keeps the
/// background task, cancellation source, and scheduling, and supplies <c>ExecuteCall</c>/
/// <c>ExecuteCallAsync</c> as the executor delegate.
/// </summary>
public static class ToolBatchRunner
{
    /// <summary>
    /// Runs each call through a synchronous executor, producing one result block per call. A throw
    /// from <paramref name="execute"/> propagates (the synchronous dispatch path surfaces it directly).
    /// </summary>
    /// <param name="calls">The dispatched tool calls.</param>
    /// <param name="execute">Executes a single call.</param>
    /// <returns>One result block per call plus the joined payload.</returns>
    public static ToolBatchResult Run(
        IReadOnlyList<ToolCallContent> calls,
        Func<ToolCallContent, ToolCallOutcome> execute)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(execute);

        var resultBlocks = new List<MessageContent>(calls.Count);
        var resultTexts = new List<string>(calls.Count);

        foreach (ToolCallContent call in calls)
        {
            ToolCallOutcome outcome = execute(call);
            resultBlocks.Add(new ToolResultContent(call.Id, outcome.Content, outcome.IsError));
            resultTexts.Add(outcome.Content);
        }

        return new ToolBatchResult(resultBlocks, string.Join(Environment.NewLine, resultTexts));
    }

    /// <summary>
    /// Runs each call through an asynchronous executor, producing one result block per call. A throw
    /// from a single call becomes that call's error result (so the others still complete); a throw that
    /// escapes the per-call guard answers every id with the error so the dispatch round can still
    /// complete. Returns null when the token is cancelled, signalling the host to emit nothing.
    /// </summary>
    /// <param name="calls">The dispatched tool calls.</param>
    /// <param name="executeAsync">Executes a single call asynchronously.</param>
    /// <param name="ct">Cancellation token; cancelled when a new batch starts or the component is removed.</param>
    /// <returns>The assembled batch, or null if cancelled.</returns>
    public static async Task<ToolBatchResult?> RunAsync(
        IReadOnlyList<ToolCallContent> calls,
        Func<ToolCallContent, CancellationToken, Task<ToolCallOutcome>> executeAsync,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(executeAsync);

        var resultBlocks = new List<MessageContent>(calls.Count);
        var resultTexts = new List<string>(calls.Count);

        try
        {
            foreach (ToolCallContent call in calls)
            {
                ToolCallOutcome outcome;
                try
                {
                    outcome = await executeAsync(call, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    outcome = new ToolCallOutcome(ex.Message, true);
                }

                resultBlocks.Add(new ToolResultContent(call.Id, outcome.Content, outcome.IsError));
                resultTexts.Add(outcome.Content);
            }
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
            {
                return null;
            }

            // Answer every dispatched id with an error so the Router round still completes.
            resultBlocks = calls.Select(c => (MessageContent)new ToolResultContent(c.Id, ex.Message, true)).ToList();
            resultTexts = new List<string> { ex.Message };
        }

        if (ct.IsCancellationRequested)
        {
            return null;
        }

        return new ToolBatchResult(resultBlocks, string.Join(Environment.NewLine, resultTexts));
    }
}
