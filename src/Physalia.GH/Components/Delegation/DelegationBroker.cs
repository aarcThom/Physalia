// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Components;

/// <summary>
/// What came back from a delegated task.
/// </summary>
/// <param name="Completed">False when the sub-pipeline never answered — a timeout, or a refusal to start.</param>
/// <param name="Text">What the Task Out node was handed, or the reason it never ran.</param>
/// <param name="Blocks">
/// The content blocks the answering signal carried, so a sub-pipeline that produced an IMAGE — a
/// snapshot, a rendered PDF region — can hand it back. The delegate returns these as tool
/// attachments, which is the machinery a tool already has for answering with something that is not
/// text.
/// </param>
internal sealed record DelegationResult(bool Completed, string Text, IReadOnlyList<MessageContent> Blocks);

/// <summary>
/// Runs a task inside another harness and waits for its answer. The mechanism behind the Delegate
/// tool, and the reason a sub-conversation is possible at all.
///
/// <para><b>Why this needed a broker rather than a wire.</b> The two ends live in different
/// Grasshopper documents — the Delegate node in the outer harness, Task In and Task Out inside the
/// inner one — and no wire crosses a harness boundary. So they are paired on the INNER DOCUMENT,
/// which both ends can name without knowing about each other: the same device the PDF registry uses
/// to pair its intake half with its reading half, and the spend ledger to pair the LLM Call with the
/// Budget Guard.</para>
///
/// <para><b>One task at a time per inner harness, and that is a real constraint rather than a
/// simplification.</b> The sub-pipeline has ONE Conversation Log, one solve state and one set of
/// latched signals; two tasks running through it at once would interleave into a single conversation
/// and neither answer would be trustworthy. Refusing the second is also the guard that stops most
/// recursion: a harness cannot delegate to one already working.</para>
///
/// <para><b>It refuses up front rather than timing out.</b> A harness with no Task In can never
/// receive the task and one with no Task Out can never answer, so both are checked before the session
/// starts. Waiting five minutes to discover a node is missing is the worst version of that
/// message.</para>
/// </summary>
internal static class DelegationBroker
{
    private static readonly object Gate = new();

    // Reference-keyed on the document object itself, which is what identity means here: two harnesses
    // are different harnesses because they own different documents.
    private static readonly Dictionary<GH_Document, Session> Active = new();

    /// <summary>
    /// Hands a task to a harness and waits for its Task Out.
    /// </summary>
    /// <param name="inner">The harness's own document.</param>
    /// <param name="task">The task text, delivered as the payload of the signal Task In mints.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <param name="ct">Cancellation; a cancelled wait comes back incomplete.</param>
    /// <returns>What the sub-pipeline answered, or why it did not.</returns>
    internal static async Task<DelegationResult> RunAsync(
        GH_Document? inner,
        string task,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (inner is null)
        {
            return Failed("The linked harness has no contents to run.");
        }

        List<TaskIn> entries = inner.Objects.OfType<TaskIn>().ToList();
        if (entries.Count == 0)
        {
            return Failed(
                "The linked harness has no Task In node, so there is no way to hand it the task. "
                + "Add one inside it and wire its Signal into the pipeline.");
        }

        if (!inner.Objects.OfType<TaskOut>().Any())
        {
            return Failed(
                "The linked harness has no Task Out node, so it could never answer. "
                + "Add one inside it and wire the end of the pipeline into it.");
        }

        Session session;
        lock (Gate)
        {
            if (Active.ContainsKey(inner))
            {
                // See the class remarks: one conversation, one task. This is also the guard that stops
                // a harness being asked to delegate to itself while it is already working.
                return Failed("That harness is already working on a task. Wait for it to finish.");
            }

            session = new Session(task);
            Active[inner] = session;
        }

        try
        {
            foreach (TaskIn entry in entries)
            {
                entry.Deliver(task);
            }

            return await session.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            lock (Gate)
            {
                Active.Remove(inner);
            }
        }
    }

    /// <summary>
    /// Reports a sub-pipeline's answer, ending the wait. Called by Task Out.
    /// </summary>
    /// <param name="inner">The harness's own document.</param>
    /// <param name="text">The answer.</param>
    /// <param name="blocks">The blocks the answering signal carried.</param>
    /// <returns>True when a delegate was waiting for this; false when nothing was.</returns>
    internal static bool Complete(GH_Document? inner, string text, IReadOnlyList<MessageContent>? blocks)
    {
        if (inner is null)
        {
            return false;
        }

        Session? session;
        lock (Gate)
        {
            Active.TryGetValue(inner, out session);
        }

        if (session is null)
        {
            return false;
        }

        // TrySetResult, not SetResult: the timeout may have got there first, and a pipeline with two
        // paths into its Task Out can answer twice.
        return session.Answer.TrySetResult(
            new DelegationResult(true, text ?? string.Empty, blocks ?? Array.Empty<MessageContent>()));
    }

    /// <summary>
    /// The task a harness is currently working on, for a caption.
    /// </summary>
    /// <param name="inner">The harness's own document.</param>
    /// <returns>The task text, or null when nothing is running.</returns>
    internal static string? RunningTask(GH_Document? inner)
    {
        if (inner is null)
        {
            return null;
        }

        lock (Gate)
        {
            return Active.TryGetValue(inner, out Session? session) ? session.Task : null;
        }
    }

    private static DelegationResult Failed(string reason) =>
        new(false, reason, Array.Empty<MessageContent>());

    private sealed class Session
    {
        internal Session(string task) => Task = task;

        internal string Task { get; }

        internal TaskCompletionSource<DelegationResult> Answer { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task<DelegationResult> WaitAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bound.CancelAfter(timeout);

            System.Threading.Tasks.Task delay =
                System.Threading.Tasks.Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, bound.Token);

            System.Threading.Tasks.Task first =
                await System.Threading.Tasks.Task.WhenAny(Answer.Task, delay).ConfigureAwait(false);

            if (ReferenceEquals(first, Answer.Task))
            {
                return await Answer.Task.ConfigureAwait(false);
            }

            return new DelegationResult(
                false,
                ct.IsCancellationRequested
                    ? "The delegated task was cancelled."
                    : $"The delegated task did not finish within {timeout.TotalSeconds:0}s. Its pipeline may be waiting on something, or have no route to its Task Out.",
                Array.Empty<MessageContent>());
        }
    }
}
