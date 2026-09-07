// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Tools;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Puts the model's question to the user as a card in the chat window, and carries the answer back to
/// the waiting tool call.
///
/// <para><b>The third sibling</b>, alongside <c>ToolApprovalBroker</c> and
/// <c>Panels.BrowserFetchOffers</c>, and the differences between the three are the whole reason they
/// are not one class. An approval BLOCKS a call and must fail closed, so every edge of it denies. A
/// fetch offer blocks nothing — the call has already failed — so it has no timeout and no default. A
/// question blocks a call like an approval, but there is no safe answer to invent, so every edge
/// returns "nobody answered" and the model is told exactly that. Folding them together would have put
/// a timeout on the one that needs none and a default on the one that must not have one.</para>
///
/// <para>Static because the window is: there is one chat window, it switches between Chats, and a
/// question can be asked from any harness in the file. Keyed by request id, so two nodes asking at
/// once queue rather than overwrite.</para>
/// </summary>
internal static class HumanQuestionBroker
{
    /// <summary>
    /// How long a question waits before it is treated as unanswered.
    ///
    /// <para>Ten minutes rather than the approval card's five. A consent decision is a yes or a no
    /// about something already described; answering a question means going and looking — at a
    /// drawing, at the model, at somebody else's email — and five minutes is not long enough to do
    /// that and come back. Affordable because the tool sets RunsAsync, so no solution is waiting.</para>
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private static readonly object Gate = new();

    private static readonly List<PendingQuestion> Waiting = new();

    /// <summary>
    /// Raised when the pending set changes, so the window can push the card the moment the model asks
    /// rather than up to a tick later.
    /// </summary>
    internal static event Action? Changed;

    /// <summary>
    /// Asks the user, and waits.
    /// </summary>
    /// <param name="question">What is being asked.</param>
    /// <param name="harness">Which harness is asking, for the card's label; may be null.</param>
    /// <param name="ct">Cancellation; a cancelled wait is unanswered.</param>
    /// <returns>The answer, or <see cref="HumanAnswer.Unanswered"/>.</returns>
    internal static async Task<HumanAnswer> AskAsync(
        HumanQuestion question,
        HarnessComponent? harness,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(question);

        // Nothing to ask through. Say so now rather than after ten minutes of silence: the answer is
        // the same and the model can say "I need someone to tell me X" while a person is still here.
        if (Components.Chat.ActiveWindow is null)
        {
            return HumanAnswer.Unanswered;
        }

        var pending = new PendingQuestion(
            Guid.NewGuid().ToString("N"),
            question,
            harness?.NickName);

        lock (Gate)
        {
            Waiting.Add(pending);
        }

        Changed?.Invoke();

        try
        {
            return await WaitAsync(pending, ct).ConfigureAwait(false);
        }
        finally
        {
            Remove(pending.Id);
        }
    }

    /// <summary>
    /// The questions currently on screen, oldest first.
    /// </summary>
    /// <returns>A snapshot of the pending set.</returns>
    internal static IReadOnlyList<PendingQuestion> Pending()
    {
        lock (Gate)
        {
            return Waiting.ToList();
        }
    }

    /// <summary>
    /// Records the user's answer to one card.
    /// </summary>
    /// <param name="id">The question's id, as the card was given it.</param>
    /// <param name="text">What the person typed or picked.</param>
    /// <param name="selectedIds">Rhino object ids selected at the moment they answered, if any.</param>
    internal static void Answer(string? id, string? text, IReadOnlyList<string>? selectedIds = null)
    {
        Find(id)?.Answer.TrySetResult(HumanAnswer.From(text ?? string.Empty, selectedIds));
    }

    /// <summary>
    /// Records that the user declined to answer one card. Distinct from an empty answer: skipping says
    /// "I am not going to tell you", which is worth the model knowing.
    /// </summary>
    /// <param name="id">The question's id.</param>
    internal static void Skip(string? id)
    {
        Find(id)?.Answer.TrySetResult(HumanAnswer.Unanswered);
    }

    /// <summary>
    /// Leaves everything outstanding unanswered. Called when the chat window closes, since the only
    /// way to answer has just gone away.
    /// </summary>
    internal static void AbandonAll()
    {
        foreach (PendingQuestion pending in Pending())
        {
            pending.Answer.TrySetResult(HumanAnswer.Unanswered);
        }
    }

    private static PendingQuestion? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (Gate)
        {
            return Waiting.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        }
    }

    private static async Task<HumanAnswer> WaitAsync(PendingQuestion pending, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        Task delay = Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, timeout.Token);
        Task first = await Task.WhenAny(pending.Answer.Task, delay).ConfigureAwait(false);

        return ReferenceEquals(first, pending.Answer.Task)
            ? await pending.Answer.Task.ConfigureAwait(false)
            : HumanAnswer.Unanswered;
    }

    private static void Remove(string id)
    {
        lock (Gate)
        {
            Waiting.RemoveAll(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        }

        Changed?.Invoke();
    }
}

/// <summary>
/// One question waiting for an answer.
/// </summary>
/// <param name="Id">Identifies this card to the page and back.</param>
/// <param name="Question">What is being asked.</param>
/// <param name="HarnessName">
/// Which harness asked. Shown on the card because the window may be looking at a different Chat than
/// the pipeline that is asking, and an unattributed question is one nobody knows how to answer.
/// </param>
internal sealed record PendingQuestion(string Id, HumanQuestion Question, string? HarnessName)
{
    /// <summary>
    /// Completed when the user answers, skips, or something answers on their behalf.
    /// </summary>
    internal TaskCompletionSource<HumanAnswer> Answer { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
