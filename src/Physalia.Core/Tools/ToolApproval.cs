// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Physalia.Core.Tools;

/// <summary>
/// Asks a person before a tool does something they may not want done.
///
/// <para>Deliberately a seam rather than a dialog belonging to one tool: downloading a file,
/// unpacking an archive and running a script are three different acts that all want the same
/// question asked, and the alternative was three prompts that would drift apart in wording and
/// behaviour.</para>
///
/// <para><b>Every edge fails CLOSED.</b> No approver attached, no window watching, the window closed
/// mid-wait, the timeout elapsed — each of those denies. A tool call is running while this is
/// pending, so the failure mode of guessing "allow" is doing the thing nobody agreed to, and the
/// failure mode of guessing "deny" is a tool result the model can react to. Only one of those is
/// recoverable.</para>
///
/// <para>The timeout is generous — minutes, not the seconds a tool usually gets — for the reason the
/// MCP sign-in flow uses five: a consent decision runs at human speed, and a person reading a URL
/// before allowing a download is doing exactly what the prompt is for.</para>
/// </summary>
public interface IToolApprover
{
    /// <summary>
    /// Puts a question to the user and waits for the answer.
    /// </summary>
    /// <param name="request">What is being asked.</param>
    /// <param name="ct">Cancellation; a cancelled wait denies.</param>
    /// <returns>True only when a person actively allowed it.</returns>
    Task<bool> RequestAsync(ToolApprovalRequest request, CancellationToken ct);
}

/// <summary>
/// One question put to the user before a tool acts.
/// </summary>
/// <param name="Title">The window title — which tool is asking.</param>
/// <param name="Summary">One line saying what is about to happen.</param>
/// <param name="Detail">
/// The specifics the decision actually turns on — the URL, the file name, the size. Shown in full
/// and never abbreviated: a prompt that hides the thing being consented to is worse than no prompt,
/// because it manufactures consent instead of asking for it.
/// </param>
public sealed record ToolApprovalRequest(string Title, string Summary, string Detail);

/// <summary>
/// The approver used when nothing has been attached: denies everything.
/// </summary>
public sealed class DeniedApprover : IToolApprover
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static DeniedApprover Instance { get; } = new();

    /// <inheritdoc/>
    public Task<bool> RequestAsync(ToolApprovalRequest request, CancellationToken ct) =>
        Task.FromResult(false);
}
