// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Recording;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Maintains the full conversation history as an append-only log and emits it as a single Signal
/// that <b>carries the full Instructions</b> (system prompt + conversation) for inference — the
/// trigger and the data are one event. The Recorder is the uncompacted source of truth: a compaction
/// component sits on the forward path (<c>Recorder → Compactor → Reasoner</c>) and only transforms the
/// copy on the signal; the Reasoner's response flows back (wirelessly, via Feedback Collector) and
/// appends to this full log.
///
/// <para>Events arrive on dedicated Signal inputs — Prompt, Response, Feedback, Tool — so the turn
/// type comes from event identity, never from conversation parity. Signals are consumed in global
/// sequence order (causal order), which guarantees a response is recorded before the feedback it
/// provoked even when both arrive in the same solve. User-side text arriving while the last turn is
/// already a user message merges into that message, preserving the role alternation providers require.
/// The outgoing Signal is minted only when a user turn was recorded — assistant turns latch quietly so
/// a Reasoner wired off this output cannot re-fire itself in an infinite loop.</para>
/// </summary>
public class Recorder : StatefulComponentBase
{
    private const int InSystemPrompt = 0;
    private const int InPromptSignal = 1;
    private const int InResponseSignal = 2;
    private const int InFeedbackSignal = 3;
    private const int InToolSignal = 4;

    private const int OutSignal = 0;

    private Conversation _conversation = Conversation.Empty;

    // Set ONLY by our own scheduled callback so the latch runs after the visible delay.
    private bool _doLatch;
    private RecordOutcome _pendingOutcome;
    private string _pendingUserText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="Recorder"/> class.
    /// </summary>
    public Recorder()
        : base("Recorder", "Rec", "Maintains the conversation history and emits it as a Signal carrying the full Instructions for inference.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("43A02F6D-D97D-4241-B4DD-067D7AE0D75E");

    /// <summary>
    /// Gets the active conversation, for display only — e.g. Prompter's chat panel. Always the full
    /// uncompacted log (compaction happens downstream, never here). Conversation is immutable, so
    /// callers cannot corrupt the log, but they must never hold the reference across solves (it is
    /// replaced on every append).
    /// </summary>
    public Conversation ActiveConversation => _conversation;

    /// <inheritdoc/>
    protected override string ClearMenuText => "Clear Conversation";

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("System Prompt", "S", "System prompt from Composer.", GH_ParamAccess.item, string.Empty);
        pManager.AddParameter(new Param_Signal(), "Prompt Signal", "PS", "Records a user turn; the signal payload is the prompt text. Use Construct Signal to combine a text payload with a manual trigger.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Response Signal", "RS", "Records an assistant turn from the Reasoner's Success Signal.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Feedback Signal", "FS", "Records feedback as a user turn. Wire one or more Feedback Collectors directly — no OR gate needed.", GH_ParamAccess.list);
        pManager.AddParameter(new Param_Signal(), "Tool Signal", "TS", "Records tool turns from a Router (via Feedback Collector): a signal whose content blocks carry tool_use is logged as an assistant turn; one whose blocks carry tool_result is logged as a user turn.", GH_ParamAccess.list);

        pManager[InPromptSignal].Optional = true;
        pManager[InResponseSignal].Optional = true;
        pManager[InFeedbackSignal].Optional = true;
        pManager[InToolSignal].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "Sig", "Latched signal minted when a user turn was recorded; it carries the full Instructions (system prompt + conversation) for inference. Wire into a Reasoner (optionally through a compaction component). Casts to Instructions/Conversation/text. Assistant turns latch quietly.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(menu, "Save Conversation", OnSaveConversation);
        Menu_AppendItem(menu, "Load Conversation", OnLoadConversation);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string systemPrompt = string.Empty;

        DA.GetData(InSystemPrompt, ref systemPrompt);

        // Observe every solve, even mid-run: events arriving while busy wait, latched on
        // their wires, and are serviced after the latch.
        ObserveSignalInputs(DA, InPromptSignal, InResponseSignal, InFeedbackSignal, InToolSignal);

        if (_doLatch)
        {
            // LATCH PASS — runs only from our own scheduled callback, after the visible delay.
            _doLatch = false;

            switch (_pendingOutcome)
            {
                case RecordOutcome.UserTurn:
                    // The minted signal carries the full Instructions: the trigger IS the data.
                    LatchSuccess(_pendingUserText, instructions: new Instructions(systemPrompt, _conversation));
                    break;
                case RecordOutcome.AssistantTurn:
                    // Quiet success: a Reasoner wired off the outgoing signal must not
                    // re-fire after its own response is recorded.
                    LatchSuccess(string.Empty, emitSignal: false);
                    break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Signal received but there was nothing new to record.");
                    LatchFailure(string.Empty, emitSignal: false);
                    break;
            }

            if (HasUnconsumedSignals(InPromptSignal, InResponseSignal, InFeedbackSignal, InToolSignal))
            {
                // Events arrived mid-run; nothing was dropped. One follow-up solve
                // services them in sequence order.
                ScheduleStateSolve(1, () => { });
            }

            EmitSignal(DA, OutSignal, SuccessSignal);
            return;
        }

        if (State != SolveState.Active)
        {
            // Consuming in global sequence order is the ordering guarantee: feedback is
            // always minted after the response that provoked it, so even when both land
            // in this one solve the assistant turn is recorded first.
            IReadOnlyList<ConsumedSignal> consumed =
                ConsumeAllSignals(InPromptSignal, InResponseSignal, InFeedbackSignal, InToolSignal);

            if (consumed.Count > 0)
            {
                EnterActive();

                var events = new List<RecordEvent>(consumed.Count);
                foreach (ConsumedSignal item in consumed)
                {
                    if (TryMapKind(item.ParamIndex, out RecordedTurnKind kind))
                    {
                        events.Add(new RecordEvent(kind, item.Signal));
                    }
                }

                RecordResult result = ConversationRecorder.Record(_conversation, events);
                _conversation = result.Conversation;
                _pendingOutcome = result.Outcome;
                _pendingUserText = result.UserTraceText;

                foreach (string warning in result.Warnings)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                }

                ScheduleStateSolve(SolveDelayMs, () => _doLatch = true);
                EmitSignal(DA, OutSignal, SuccessSignal);
                return;
            }
        }

        // Idle solve — re-emit the latched signal.
        EmitSignal(DA, OutSignal, SuccessSignal);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _conversation = Conversation.Empty;
        _doLatch = false;
        _pendingOutcome = RecordOutcome.Nothing;
        _pendingUserText = string.Empty;
    }

    // Maps a Signal input index to the turn kind it designates. Turn type comes from input
    // identity — never from conversation parity.
    private static bool TryMapKind(int paramIndex, out RecordedTurnKind kind)
    {
        switch (paramIndex)
        {
            case InPromptSignal: kind = RecordedTurnKind.Prompt; return true;
            case InResponseSignal: kind = RecordedTurnKind.Response; return true;
            case InFeedbackSignal: kind = RecordedTurnKind.Feedback; return true;
            case InToolSignal: kind = RecordedTurnKind.Tool; return true;
            default: kind = default; return false;
        }
    }

    private void OnSaveConversation(object? sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Save Conversation is not yet implemented.");
        ExpireSolution(true);
    }

    private void OnLoadConversation(object? sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Load Conversation is not yet implemented.");
        ExpireSolution(true);
    }
}
