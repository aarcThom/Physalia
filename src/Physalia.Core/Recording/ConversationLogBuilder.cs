// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;

namespace Physalia.Core.Recording;

/// <summary>
/// The kind of turn a recorded signal represents, taken from the input it arrived on — never from
/// conversation parity.
/// </summary>
public enum RecordedTurnKind
{
    /// <summary>A user prompt.</summary>
    Prompt,

    /// <summary>An assistant response.</summary>
    Response,

    /// <summary>Feedback recorded as a user turn.</summary>
    Feedback,

    /// <summary>A tool turn (tool_use as assistant, tool_result as user).</summary>
    Tool,
}

/// <summary>
/// What the recording produced overall, used by the host to decide how to latch the outgoing signal.
/// </summary>
public enum RecordOutcome
{
    /// <summary>Nothing new was recorded.</summary>
    Nothing,

    /// <summary>A user turn was recorded — the host should mint a signal carrying the Instructions.</summary>
    UserTurn,

    /// <summary>Only an assistant turn was recorded — the host should latch quietly (no re-fire).</summary>
    AssistantTurn,
}

/// <summary>
/// One signal to record, paired with the kind of turn its input designates.
/// </summary>
/// <param name="Kind">The turn kind (from the originating input).</param>
/// <param name="Signal">The consumed signal.</param>
public sealed record RecordEvent(RecordedTurnKind Kind, PhySignal Signal);

/// <summary>
/// The result of applying a batch of events to a conversation: the new conversation, the overall
/// outcome, the trace text of the last user turn, and any warnings the host should surface.
/// </summary>
/// <param name="Conversation">The conversation after recording.</param>
/// <param name="Outcome">The overall outcome (drives latch behaviour).</param>
/// <param name="UserTraceText">The trace text of the recorded user turn (payload for the minted signal).</param>
/// <param name="Warnings">Human-readable warnings produced while recording.</param>
public sealed record RecordResult(
    Conversation Conversation,
    RecordOutcome Outcome,
    string UserTraceText,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Pure turn-assembly policy for the Conversation Log. Given the current conversation and a batch of events
/// in causal (sequence) order, it appends each as the right kind of turn — merging consecutive
/// user-side turns to preserve the strict role alternation providers require, recording an
/// assistant tool-call request before its user tool-result, and reporting whether a user turn (which
/// should fire inference) or only an assistant turn (which should latch quietly) was recorded. It
/// performs no I/O and touches no Grasshopper state; the host maps inputs to <see cref="RecordEvent"/>
/// and surfaces the returned warnings.
/// </summary>
public static class ConversationLogBuilder
{
    /// <summary>
    /// Records a batch of events onto a conversation.
    /// </summary>
    /// <param name="current">The conversation before recording.</param>
    /// <param name="events">The events to record, in causal (sequence) order.</param>
    /// <returns>The new conversation plus the outcome, trace text, and warnings.</returns>
    public static RecordResult Record(Conversation current, IReadOnlyList<RecordEvent> events)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(events);

        var state = new State(current);
        foreach (RecordEvent ev in events)
        {
            state.Apply(ev);
        }

        return new RecordResult(state.Conversation, state.Outcome, state.UserTraceText, state.Warnings);
    }

    private sealed class State
    {
        public State(Conversation conversation) => Conversation = conversation;

        public Conversation Conversation { get; private set; }

        public RecordOutcome Outcome { get; private set; } = RecordOutcome.Nothing;

        public string UserTraceText { get; private set; } = string.Empty;

        public List<string> Warnings { get; } = new();

        public void Apply(RecordEvent ev)
        {
            PhySignal signal = ev.Signal;
            switch (ev.Kind)
            {
                case RecordedTurnKind.Response:
                    RecordAssistantTurn(signal);
                    break;

                case RecordedTurnKind.Prompt:
                    // A Chat turn may carry resolved content blocks (text + inline images);
                    // a Construct Signal carries only the text payload. An images-only prompt has
                    // blocks but a blank payload, so check blocks first.
                    if (signal.ContentBlocks.Count > 0)
                    {
                        RecordUserBlocks(signal.ContentBlocks, signal.Payload);
                        break;
                    }

                    if (!StringHelpers.IsNonBlank(signal.Payload))
                    {
                        Warnings.Add("Prompt signal carried no text — use Construct Signal to attach a payload to a manual trigger.");
                        break;
                    }

                    RecordUserText(signal.Payload);
                    break;

                case RecordedTurnKind.Feedback:
                    // Feedback may carry resolved content blocks (e.g. a Geometry Observation's image
                    // alongside its message) just like a prompt; record them so the image survives.
                    // An image-only feedback turn has blocks but a blank payload, so check blocks first.
                    if (signal.ContentBlocks.Count > 0)
                    {
                        RecordUserBlocks(signal.ContentBlocks, signal.Payload, isFeedback: true);
                        break;
                    }

                    if (!StringHelpers.IsNonBlank(signal.Payload))
                    {
                        Warnings.Add("Feedback signal received with an empty payload.");
                        break;
                    }

                    RecordUserText(signal.Payload, isFeedback: true);
                    break;

                case RecordedTurnKind.Tool:
                    RecordToolSignal(signal);
                    break;
            }
        }

        private void RecordToolSignal(PhySignal signal)
        {
            IReadOnlyList<MessageContent> blocks = signal.ContentBlocks;
            if (blocks.Count == 0)
            {
                Warnings.Add("Tool signal carried no content blocks.");
                return;
            }

            // Split into the assistant request (text + tool_use) and the tool results, so a single
            // signal that happens to carry both — e.g. if a Feedback Collector batched them — records
            // the assistant turn first and the tool_result turn second, never dropping either.
            var requestBlocks = blocks.Where(b => b is not ToolResultContent).ToList();
            var resultBlocks = blocks.OfType<ToolResultContent>().Cast<MessageContent>().ToList();

            bool recorded = false;

            if (requestBlocks.Any(b => b is ToolCallContent))
            {
                RecordAssistantBlocks(requestBlocks);
                recorded = true;
            }

            if (resultBlocks.Count > 0)
            {
                RecordUserBlocks(resultBlocks, signal.Payload);
                recorded = true;
            }

            if (!recorded)
            {
                Warnings.Add("Tool signal had no tool_use or tool_result blocks.");
            }
        }

        private void RecordAssistantTurn(PhySignal signal)
        {
            if (!StringHelpers.IsNonBlank(signal.Payload))
            {
                Warnings.Add("Response signal received but it carried no text.");
                return;
            }

            var message = new ConversationMessage(Role.Assistant, signal.Payload);

            try
            {
                // Guard only: under sequence ordering an assistant turn cannot legally follow
                // another assistant turn, so a throw here indicates a wiring mistake.
                Conversation = Conversation.Append(message);

                if (Outcome == RecordOutcome.Nothing)
                {
                    Outcome = RecordOutcome.AssistantTurn;
                }
            }
            catch (InvalidOperationException ex)
            {
                Warnings.Add(ex.Message);
            }
        }

        private void RecordAssistantBlocks(IReadOnlyList<MessageContent> blocks)
        {
            try
            {
                var message = new ConversationMessage(Role.Assistant, blocks);

                // Guard only: under sequence ordering an assistant turn cannot legally follow
                // another assistant turn, so a throw here indicates a wiring mistake.
                Conversation = Conversation.Append(message);

                // Quiet: recording the model's tool-call request must not fire the outgoing Signal
                // (the LLM Call re-runs only once the tool results are recorded as a user turn).
                if (Outcome == RecordOutcome.Nothing)
                {
                    Outcome = RecordOutcome.AssistantTurn;
                }
            }
            catch (InvalidOperationException ex)
            {
                Warnings.Add(ex.Message);
            }
        }

        private void RecordUserText(string text, bool isFeedback = false) =>
            RecordUserBlocks(new MessageContent[] { new TextContent(text) }, text, isFeedback);

        private void RecordUserBlocks(IReadOnlyList<MessageContent> blocks, string traceText, bool isFeedback = false)
        {
            try
            {
                // Merging preserves the strict role alternation providers require when two user-side
                // events arrive in a row (e.g. a prompt followed by feedback before any assistant turn).
                Conversation = Conversation.Count > 0 && Conversation.Messages[^1].Role == Role.User
                    ? Conversation.MergeIntoLastUserMessage(blocks)
                    : Conversation.Append(new ConversationMessage(Role.User, blocks) { IsFeedback = isFeedback });
                Outcome = RecordOutcome.UserTurn;
                UserTraceText = traceText;
            }
            catch (InvalidOperationException ex)
            {
                Warnings.Add(ex.Message);
            }
        }
    }
}
