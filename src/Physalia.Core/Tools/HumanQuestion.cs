// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Physalia.Core.Tools;

/// <summary>
/// What kind of answer a question is waiting for.
/// </summary>
public enum HumanAnswerKind
{
    /// <summary>Typed prose.</summary>
    Text,

    /// <summary>One of a short list of options the model supplied.</summary>
    Choice,

    /// <summary>
    /// A selection made in Rhino. The card asks the person to select the objects they mean and press
    /// a button; what comes back is what was selected at that moment. The one answer a chat window
    /// cannot hold, and the reason this seam is not just a text box.
    /// </summary>
    RhinoSelection,
}

/// <summary>
/// One question the model is putting to the person.
///
/// <para>Sibling of <see cref="ToolApprovalRequest"/> and deliberately not the same thing. An
/// approval asks whether the model may do something it has already decided on, and the only answers
/// are yes and no; this asks for information the model does not have and cannot get. So the failure
/// modes differ too: an unanswered approval must be a No, while an unanswered question is simply
/// unanswered, and the model is told so and left to decide what to do about it.</para>
/// </summary>
/// <param name="Title">Which tool is asking — the card's heading.</param>
/// <param name="Prompt">The question itself, in the model's own words.</param>
/// <param name="Kind">What sort of answer is expected.</param>
/// <param name="Choices">
/// The options, for <see cref="HumanAnswerKind.Choice"/>. Empty otherwise. A choice question still
/// accepts typed prose — a list of options is the model's guess at the answer space, and being
/// unable to say "none of those, because…" is how a wrong guess becomes a wrong answer.
/// </param>
public sealed record HumanQuestion(
    string Title,
    string Prompt,
    HumanAnswerKind Kind,
    IReadOnlyList<string> Choices)
{
    /// <summary>
    /// Creates a question expecting typed prose.
    /// </summary>
    /// <param name="title">Which tool is asking.</param>
    /// <param name="prompt">The question.</param>
    /// <returns>The question.</returns>
    public static HumanQuestion Text(string title, string prompt) =>
        new(title, prompt, HumanAnswerKind.Text, System.Array.Empty<string>());
}

/// <summary>
/// What came back.
/// </summary>
/// <param name="Answered">
/// False when nobody answered — no window to ask through, the window closed, the round was
/// cancelled, or the wait ran out. Distinct from an empty <see cref="Text"/>, which is a person
/// deliberately answering with nothing.
/// </param>
/// <param name="Text">What the person said, or the option they picked.</param>
/// <param name="SelectedIds">
/// The Rhino object ids selected when the person pressed the button, for a
/// <see cref="HumanAnswerKind.RhinoSelection"/> question. Empty otherwise. On the wire as well as in
/// the answer, because the point of asking is usually to act on those objects.
/// </param>
public sealed record HumanAnswer(bool Answered, string Text, IReadOnlyList<string> SelectedIds)
{
    /// <summary>
    /// Gets the answer used when nobody was there.
    /// </summary>
    public static HumanAnswer Unanswered { get; } =
        new(false, string.Empty, System.Array.Empty<string>());

    /// <summary>
    /// Creates an answered response.
    /// </summary>
    /// <param name="text">What the person said.</param>
    /// <param name="selectedIds">Any Rhino ids that came with it.</param>
    /// <returns>The answer.</returns>
    public static HumanAnswer From(string text, IReadOnlyList<string>? selectedIds = null) =>
        new(true, text ?? string.Empty, selectedIds ?? System.Array.Empty<string>());
}

/// <summary>
/// Puts a question to a person and waits for the answer.
///
/// <para>A seam for the same reason <see cref="IToolApprover"/> is one: asking the human is not one
/// tool's business. The Ask Human tool is the first caller, but a guardrail wanting a judgement call
/// and a transmitter wanting a target picked are the same question asked from somewhere else, and
/// three dialogs would drift apart in wording and behaviour.</para>
///
/// <para><b>Every edge is UNANSWERED, not a guess.</b> No window, the window closed, the round
/// cancelled, the wait elapsed — each returns <see cref="HumanAnswer.Unanswered"/>, and the tool
/// tells the model plainly that nobody was there. Inventing an answer would be the one truly
/// unrecoverable failure available here: the model would carry on as though a person had agreed to
/// something.</para>
/// </summary>
public interface IHumanAsker
{
    /// <summary>
    /// Puts a question to the user and waits.
    /// </summary>
    /// <param name="question">What is being asked.</param>
    /// <param name="ct">Cancellation; a cancelled wait is unanswered.</param>
    /// <returns>The answer, or <see cref="HumanAnswer.Unanswered"/>.</returns>
    Task<HumanAnswer> AskAsync(HumanQuestion question, CancellationToken ct);
}

/// <summary>
/// The asker used when nothing has been attached: answers nothing.
/// </summary>
public sealed class UnansweredAsker : IHumanAsker
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static UnansweredAsker Instance { get; } = new();

    /// <inheritdoc/>
    public Task<HumanAnswer> AskAsync(HumanQuestion question, CancellationToken ct) =>
        Task.FromResult(HumanAnswer.Unanswered);
}
