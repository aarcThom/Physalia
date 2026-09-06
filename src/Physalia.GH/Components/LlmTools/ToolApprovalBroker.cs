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
/// Puts a tool's approval question to the user as a card in the chat window, and carries the answer
/// back to the waiting tool call.
///
/// <para><b>Why the chat window rather than a Rhino dialog.</b> An approval is part of a turn: the
/// model asked for something, and the person is being asked whether it may have it. Putting that in
/// the conversation keeps the request next to what prompted it, keeps the whole exchange in one
/// place, and — the practical half — a modal Rhino dialog is a window that can end up behind Rhino,
/// on another monitor, or over a canvas the user was not looking at.</para>
///
/// <para><b>Every edge denies, and the set of edges is what makes this careful.</b> No chat window
/// open at all denies IMMEDIATELY rather than waiting out the timeout — there is nowhere to ask, and
/// making the user wait five minutes to be told no is worse than telling them now. The window closing
/// mid-wait denies. The round being cancelled denies. The timeout denies. The failure mode of
/// guessing "allow" is doing the thing nobody agreed to; the failure mode of guessing "deny" is a
/// tool result the model can react to, and only one of those can be taken back.</para>
///
/// <para>Static because the window is: there is one chat window, it switches between Chats, and a
/// tool call can be running against any harness in the file. Requests are keyed by their own id, so
/// two nodes asking at once queue up rather than overwrite one another.</para>
/// </summary>
internal static class ToolApprovalBroker
{
    /// <summary>
    /// How long a question waits before it is treated as a No.
    ///
    /// <para>Five minutes, matching the MCP sign-in flow rather than a tool's usual two, because a
    /// consent decision runs at human speed — someone reading a URL before allowing a download is
    /// doing exactly what the card is for. The bound exists only so a card nobody answers cannot hold
    /// a tool id open forever.</para>
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private static readonly object Gate = new();

    private static readonly List<PendingApproval> Waiting = new();

    /// <summary>
    /// Raised when the pending set changes, so the window can push it to the page without waiting
    /// for its next tick. The tick would pick it up anyway; this makes a card appear the moment the
    /// model asks rather than up to a tick later.
    /// </summary>
    internal static event Action? Changed;

    /// <summary>
    /// Asks the user, and waits.
    /// </summary>
    /// <param name="request">What is being asked.</param>
    /// <param name="harness">Which harness is asking, for the card's label; may be null.</param>
    /// <param name="ct">Cancellation; a cancelled wait denies.</param>
    /// <returns>True only when a person actively allowed it.</returns>
    internal static async Task<bool> RequestAsync(
        ToolApprovalRequest request,
        HarnessComponent? harness,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Nothing to ask through. Deny now rather than after five minutes of silence: the answer is
        // the same and the model can act on it while the user is still here.
        if (Components.Chat.ActiveWindow is null)
        {
            return false;
        }

        var pending = new PendingApproval(
            Guid.NewGuid().ToString("N"),
            request,
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
    internal static IReadOnlyList<PendingApproval> Pending()
    {
        lock (Gate)
        {
            return Waiting.ToList();
        }
    }

    /// <summary>
    /// Records the user's answer to one card.
    /// </summary>
    /// <param name="id">The request's id, as the card was given it.</param>
    /// <param name="allow">What the user chose.</param>
    internal static void Answer(string? id, bool allow)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        PendingApproval? pending;
        lock (Gate)
        {
            pending = Waiting.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        }

        // TrySetResult, not SetResult: a card can be answered twice if the page double-fires, and the
        // timeout may have got there first.
        pending?.Answer.TrySetResult(allow);
    }

    /// <summary>
    /// Denies everything outstanding. Called when the chat window closes, since the only way to
    /// answer has just gone away.
    /// </summary>
    internal static void DenyAll()
    {
        foreach (PendingApproval pending in Pending())
        {
            pending.Answer.TrySetResult(false);
        }
    }

    private static async Task<bool> WaitAsync(PendingApproval pending, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        Task delay = Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, timeout.Token);
        Task first = await Task.WhenAny(pending.Answer.Task, delay).ConfigureAwait(false);

        return ReferenceEquals(first, pending.Answer.Task) && await pending.Answer.Task.ConfigureAwait(false);
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
/// One approval question waiting for an answer.
/// </summary>
/// <param name="Id">Identifies this card to the page and back.</param>
/// <param name="Request">What is being asked.</param>
/// <param name="HarnessName">
/// Which harness asked. Shown on the card because the window may be looking at a different Chat than
/// the one whose pipeline is asking, and "something wants to download a file" without saying what is
/// a question nobody can answer well.
/// </param>
internal sealed record PendingApproval(string Id, ToolApprovalRequest Request, string? HarnessName)
{
    /// <summary>
    /// Completed when the user answers, or when something denies on their behalf.
    /// </summary>
    internal TaskCompletionSource<bool> Answer { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
