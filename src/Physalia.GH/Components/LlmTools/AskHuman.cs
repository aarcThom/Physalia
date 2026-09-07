// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tools;

namespace Physalia.GH.Components;

/// <summary>
/// Lets the model ask the person a question and wait for the answer. The inverse of Declare, and the
/// thing that makes a pipeline cooperative rather than one-shot.
///
/// <para><b>What existed before this was yes and no.</b> The approval card can ask whether the model
/// may do something it has already decided on; nothing could ask for information it does not have.
/// So a model missing one fact — which of two structural systems, what the cladding weighs, whether
/// the site boundary in the file is the current one — had two options: guess, or stop and hope
/// somebody read the transcript. Both waste the round, and guessing wastes everything built on
/// it.</para>
///
/// <para><b>Selecting in Rhino is the case that justifies the seam.</b> A question whose answer is
/// "those ones" cannot be typed, and it is the commonest question in a CAD conversation. Asking for a
/// Rhino selection puts a button on the card, reads what is selected when it is pressed, and hands
/// the ids back — to the model in the answer, and to the definition on the <b>Selection</b>
/// output.</para>
///
/// <para><b>Nobody answering is not the same as an answer, and never becomes one.</b> No chat window,
/// the window closing, the round being cancelled, ten minutes of silence — all of those come back as
/// unanswered, and the model is told so in as many words. This is the one place where inventing a
/// plausible default would be worse than any timeout: the model would proceed as though a person had
/// agreed to something.</para>
///
/// <para>It is a genuine wait, so <see cref="RunsAsync"/> is true — not for speed but because a
/// person reading a drawing before answering must not be holding a Grasshopper solution open.</para>
/// </summary>
public class AskHuman : LlmToolComponentBase
{
    private static readonly int OutAnswer = FirstAdditionalOutputIndex;
    private static readonly int OutSelection = FirstAdditionalOutputIndex + 1;

    private static readonly LlmToolDefinition ToolDef = new(
        "ask_human",
        "Ask the person a question and wait for their answer. Use it when you are missing something only they can supply — a preference between real alternatives, a fact about the project that is not in the file, or which objects they mean. Set expect to \"rhino_selection\" to have them select objects in Rhino instead of typing; the answer then includes the object ids. Ask one thing at a time, and ask rather than guessing when a wrong guess would waste the work built on it. If nobody answers you will be told so plainly — do not treat that as agreement.",
        "{\"type\":\"object\",\"properties\":"
        + "{\"question\":{\"type\":\"string\",\"description\":\"The question, in plain language. One question, and say why you are asking if it is not obvious.\"},"
        + "\"choices\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Optional short list of options to offer as buttons. The person can still type something else, so offer these as your best guesses rather than as the only answers.\"},"
        + "\"expect\":{\"type\":\"string\",\"enum\":[\"text\",\"rhino_selection\"],\"description\":\"What sort of answer you want. \\\"rhino_selection\\\" asks them to select objects in Rhino and returns the selected object ids.\",\"default\":\"text\"}},"
        + "\"required\":[\"question\"]}");

    private string _answer = string.Empty;

    private List<string> _selection = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AskHuman"/> class.
    /// </summary>
    public AskHuman()
        : base(
            "Ask Human",
            "Ask",
            "Lets the model ask you a question and wait for your answer — typed, one of a few options, or a selection you make in Rhino. The question appears as a card in the chat window.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4A0E85D7-3F62-4C19-B8D4-27C6905AE3B1");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "A question the model wants to put to you, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises asking you to the model. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "Your answer heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    /// <remarks>
    /// A person may be reading a drawing, walking to someone's desk, or looking at the model. None of
    /// that can happen with a Grasshopper solution held open, which is the same reason Take Snapshot
    /// and Read PDF are async — though unlike Take Snapshot there is nothing here to marshal onto
    /// Rhino's idle loop except the selection read, which happens on the answer.
    /// </remarks>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Answer",
            "A",
            "What you last told the model, so the definition can show or log it without going through the conversation.",
            GH_ParamAccess.item);
        pManager.AddTextParameter(
            "Selection",
            "Sel",
            "The Rhino object ids you had selected when you answered a select-in-Rhino question, one per item. Wire into a script or a Rhino reference to act on exactly those objects.",
            GH_ParamAccess.list);
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        // From OnSolveEnd, not OnSolveTick: the call is what sets these, and OnSolveTick runs before
        // it. Re-published unconditionally so they stay on the wire between solves.
        da.SetData(OutAnswer, _answer);
        da.SetDataList(OutSelection, _selection);
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        string question;
        var choices = new List<string>();
        string expect = "text";

        try
        {
            using JsonDocument args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.InputJson) ? "{}" : call.InputJson);

            question = args.RootElement.TryGetProperty("question", out JsonElement q) ? q.GetString() ?? string.Empty : string.Empty;

            if (args.RootElement.TryGetProperty("expect", out JsonElement e))
            {
                expect = e.GetString() ?? "text";
            }

            if (args.RootElement.TryGetProperty("choices", out JsonElement c) && c.ValueKind == JsonValueKind.Array)
            {
                choices.AddRange(c.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()));
            }
        }
        catch (JsonException ex)
        {
            return ToolCallResult.Error($"The arguments were not valid JSON: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return ToolCallResult.Error("ask_human requires a non-empty 'question'.");
        }

        HumanAnswerKind kind = string.Equals(expect, "rhino_selection", StringComparison.OrdinalIgnoreCase)
            ? HumanAnswerKind.RhinoSelection
            : choices.Count > 0 ? HumanAnswerKind.Choice : HumanAnswerKind.Text;

        var request = new HumanQuestion("Ask Human", question.Trim(), kind, choices);

        HumanAnswer answer = await HumanQuestionBroker
            .AskAsync(request, Harness.PhyDocuments.Harness(this), ct)
            .ConfigureAwait(false);

        if (!answer.Answered)
        {
            // Never dressed up as an answer. The model is told the state of the world — nobody
            // replied — and left to decide, which is usually to state the assumption it is about to
            // make so the person can correct it later.
            return ToolCallResult.Error(
                "Nobody answered. The chat window may not be open, or the question timed out after ten minutes. "
                + "Do not treat this as agreement: either carry on and say plainly which assumption you are "
                + "making, or stop and say what you need.");
        }

        _answer = answer.Text;
        _selection = answer.SelectedIds.ToList();

        return ToolCallResult.Ok(Describe(kind, answer));
    }

    /// <inheritdoc/>
    protected override void ClearStateOutputs()
    {
        _answer = string.Empty;
        _selection = new List<string>();
    }

    private static string Describe(HumanAnswerKind kind, HumanAnswer answer)
    {
        var text = new StringBuilder();

        if (kind == HumanAnswerKind.RhinoSelection)
        {
            text.Append(answer.SelectedIds.Count switch
            {
                0 => "The user answered with nothing selected in Rhino.",
                1 => "The user selected 1 object in Rhino.",
                _ => $"The user selected {answer.SelectedIds.Count} objects in Rhino.",
            });

            if (!string.IsNullOrWhiteSpace(answer.Text))
            {
                text.Append(" They also said: ").Append(answer.Text.Trim());
            }

            if (answer.SelectedIds.Count > 0)
            {
                // The ids matter because run_rhino_script can act on them directly, which is the
                // point of asking this way rather than asking for a description.
                text.Append("\n\nObject ids (usable from run_rhino_script, and on this node's Selection output):\n");
                text.Append(string.Join("\n", answer.SelectedIds.Take(200)));

                if (answer.SelectedIds.Count > 200)
                {
                    text.Append($"\n… and {answer.SelectedIds.Count - 200} more, all on the Selection output.");
                }
            }

            return text.ToString();
        }

        return string.IsNullOrWhiteSpace(answer.Text)
            ? "The user answered, but said nothing. Take that as \"no preference\" rather than as agreement to anything specific."
            : "The user answered: " + answer.Text.Trim();
    }
}
