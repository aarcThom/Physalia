// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Compaction;
using Physalia.Core.Config;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.Core.Providers.ClaudeCode;
using Physalia.Core.Providers.Codex;
using Physalia.Core.Signals;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Core inference component. Receives Instructions from the Conversation Log and performs a single
/// forward pass, streaming the result. Stateless between calls — all context lives in
/// the Conversation Log. Routes the response forward on the Success Signal or an API error back on
/// the Fail Signal through <see cref="RoutingComponentBase{TData}"/>.
/// </summary>
public class LlmCall : RoutingComponentBase<Instructions>, IStreamingTextSource
{
    /// <summary>
    /// How many tool-pairing defects the repair warning names before it summarises the rest. One
    /// broken cut usually produces several, and a canvas balloon has to stay readable.
    /// </summary>
    private const int MaxReportedPairingProblems = 3;

    private int _cancelIndex = -1;
    private bool _lastCancel;
    private bool _isRunning;
    private string _response = string.Empty;
    private string? _apiError;
    private LlmErrorKind? _apiErrorKind;
    private string? _stopReason;

    // Set when the incoming conversation had to be repaired before it could be sent (see
    // PushSolve). Surfaced as a canvas warning on the read pass — never silently, because a
    // repair means something upstream produced a conversation no provider would accept.
    private string? _repairWarning;

    // Token usage from the last completed call. Reported as a canvas remark because the
    // cache-read figure is the ONLY way to confirm the system prompt's cacheable prefix is
    // actually being reused: `input_tokens` counts the uncached remainder only, so a working
    // cache would otherwise show up as nothing more than a mysterious drop in prompt size.
    private LlmUsage? _usage;
    private IReadOnlyList<LlmToolCall>? _toolCalls;
    private CancellationTokenSource? _cts;

    // Live streaming buffer: the response text accumulated so far this run. Appended on the
    // background inference thread and read by the Chat window on the UI thread, so every access
    // is guarded by the lock. Null between runs; the window shows it only while this is IsBusy.
    private readonly object _streamLock = new();
    private StringBuilder? _streamBuffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmCall"/> class.
    /// </summary>
    public LlmCall()
        : base("LLM Call", "LLM Call", "Asks the model for one reply and streams it into the chat window as it arrives. One reply per arriving signal — nothing repeats or retries on its own.", "Pipeline")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F1097B2B-564A-43F8-8F70-BA6961F00E00");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The conversation to send. Wire a Conversation Log's Signal here — the signal brings the instructions and history with it, so there is no second wire for the text.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "The model's finished reply. Wire it back into a Conversation Log's Response Signal so the answer is remembered, and onward to whatever reads it.";

    /// <inheritdoc/>
    protected override string FailSignalDescription =>
        "Fires when the call could not be made or completed — no connection, a rejected key, a request the provider turned down — carrying the reason. The model said nothing.";

    /// <inheritdoc/>
    /// <remarks>
    /// The Chat window reads this while the component IsBusy to render the response as it streams.
    /// Null until the first token arrives; dropped at the start of each run by
    /// <see cref="ClearStateOutputs"/>.
    /// </remarks>
    public string? StreamingText
    {
        get
        {
            lock (_streamLock)
            {
                return _streamBuffer is { Length: > 0 } buffer ? buffer.ToString() : null;
            }
        }
    }

    /// <inheritdoc/>
    protected override bool AutoScheduleRead => false;

    /// <inheritdoc/>
    /// <remarks>Drops the previous run's streaming text the moment a new run goes Active.</remarks>
    protected override void ClearStateOutputs()
    {
        lock (_streamLock)
        {
            _streamBuffer = null;
        }
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Which model to ask. Wire a Model component, or a Tweaker if you have adjusted its settings.", GH_ParamAccess.item);
        _cancelIndex = pManager.AddBooleanParameter("Cancel", "X", "A press abandons the reply currently being written. Nothing is recorded and no signal goes out.", GH_ParamAccess.item, false);
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Tool Calls", "TC", "Fires instead of Success when the model wants to use a tool rather than answer. Wire into a Router, which runs the tool and sends the result back for another turn.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    /// <remarks>The Tool Calls output registered by <see cref="RegisterAdditionalOutputs"/> sits at index 2.</remarks>
    protected override int AuxOutputIndex => 2;

    /// <inheritdoc/>
    /// <remarks>
    /// The full inference context rides on the consumed signal itself (the Conversation Log mints a signal
    /// carrying Instructions; a compaction component re-emits one carrying compacted Instructions).
    /// The trigger IS the data — there is no separate Instructions input.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out Instructions data)
    {
        data = default!;
        if (signal.Instructions is not Instructions instructions)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Signal carried no Instructions — wire a Conversation Log (optionally through a compaction component) into this input.");
            return false;
        }

        data = instructions;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        bool cancel = false;
        if (_cancelIndex >= 0)
        {
            da.GetData(_cancelIndex, ref cancel);
        }

        if (cancel && !_lastCancel)
        {
            CancelInference();
        }

        _lastCancel = cancel;
    }

    /// <summary>
    /// Cancels the active inference call, if one is in flight. Fired by the rising edge of the
    /// Cancel input and by the chat window's cancel button (routed through the wired Conversation Log via
    /// <see cref="PromptPipelineView.CancelPipeline"/>). No-op when no request is running.
    /// </summary>
    public void CancelInference()
    {
        if (!_isRunning)
        {
            return;
        }

        _cts?.Cancel();
        _isRunning = false;
        AbortReadPass();
    }

    /// <inheritdoc/>
    /// <remarks>Starts the async inference. The read pass fires from the completion callback, not a fixed delay.</remarks>
    protected override void PushSolve(Instructions data, IGH_DataAccess da)
    {
        _apiError = null;
        _apiErrorKind = null;
        _repairWarning = null;
        _response = string.Empty;
        _stopReason = null;
        _usage = null;
        _toolCalls = null;

        var modelGoo = new GH_ModelConfig();
        if (!da.GetData(0, ref modelGoo) || modelGoo.Value is not ModelConfig config)
        {
            _apiError = "No valid Model configuration connected.";
            RequestReadPass();
            return;
        }

        // Last line of defence before the wire. A conversation whose tool exchanges are not
        // paired is rejected outright by every provider, which halts the whole loop over
        // something the model did nothing to cause — a compactor that cut a tool exchange, a
        // Router round that never completed, a turn the log had to drop. Repair the copy we are
        // about to send (the Conversation Log keeps the uncompacted original) and say so out loud.
        data = RepairToolPairing(data);

        // Stamp this component's identity so stateful providers (Claude Code's warm-process
        // pool) can keep one long-lived session per LLM Call across forward passes.
        config = config with { SessionKey = InstanceGuid };

        // The tool definitions ride on the consumed signal's Instructions (the Conversation Log lifts them
        // there from the Tools Present grounding), so there is no separate Tools input to read.
        StartInference(data, config, data.Tools);
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(Instructions data, IGH_DataAccess da)
    {
        if (_apiError != null)
        {
            // An InvalidRequest is a fault in the request Physalia built, not in anything the
            // model said — say so, because the reflex on seeing a red LLM Call is to blame the
            // model or the prompt and neither is at fault here.
            string described = _apiErrorKind == LlmErrorKind.InvalidRequest
                ? "The request Physalia sent was rejected as malformed — this is a Physalia-side "
                    + $"fault, not something the model can fix. {_apiError}"
                : _apiError;

            return RoutingResult.Fail(described, described, GH_RuntimeMessageLevel.Error);
        }

        // A repair warning describes the request that went out, not the response that came back,
        // so it seeds the message instead of competing with the response-side warnings below.
        string? warning = _repairWarning;

        // Truncation names the actionable fix, so it wins over the thinking-only warning.
        if (StopReasons.IsTruncation(_stopReason))
        {
            warning = AppendWarning(warning, "Response was truncated at the max token limit — raise Max Tokens (or lower the Thinking Budget) on the model component.");

            // Truncated AND nothing usable after stripping thinking: routing this forward as
            // Success would hand downstream an empty payload that dead-ends quietly (the exact
            // silent-stall failure of the 2026-07-13 session). Fail the round with corrective
            // feedback instead, so a wired feedback loop retries and an unwired one at least
            // shows a failed component instead of nothing.
            if (_toolCalls is not { Count: > 0 } && !StringHelpers.IsNonBlank(ThinkingTags.Strip(_response)))
            {
                return RoutingResult.Fail(
                    "Your previous response hit the maximum token limit while still reasoning and "
                    + "was cut off before any answer text was produced — nothing downstream received "
                    + "it, and nothing was placed or changed. Re-send your ENTIRE response now: keep "
                    + "the reasoning brief and produce the answer immediately, because the same token "
                    + "limit applies to this attempt.",
                    warning,
                    GH_RuntimeMessageLevel.Warning);
            }
        }
        else if (_toolCalls is not { Count: > 0 }
            && StringHelpers.IsNonBlank(_response)
            && !StringHelpers.IsNonBlank(ThinkingTags.Strip(_response)))
        {
            // Only a warning when there is nothing else to show for the round. A response that is
            // pure thinking plus tool calls is exactly how a tool-using turn is supposed to look.
            warning = AppendWarning(warning, "The model spent its entire response thinking and produced no answer text.");
        }

        if (_toolCalls is { Count: > 0 } calls)
        {
            // The model asked for tools. Build the assistant turn (optional text + one tool_use
            // block per call) and route it on the aux (Tool Calls) output for a Router to dispatch.
            var blocks = new List<MessageContent>();
            if (!string.IsNullOrEmpty(_response))
            {
                blocks.Add(new TextContent(_response));
            }

            foreach (LlmToolCall call in calls)
            {
                blocks.Add(new ToolCallContent(call.Id, call.Name, call.InputJson));
            }

            PhySignal signal = PhySignal.Mint(SignalOutcome.Success, _response, InstanceGuid, Name, blocks);
            return warning is null
                ? RoutingResult.Aux(signal)
                : RoutingResult.Aux(signal, warning, GH_RuntimeMessageLevel.Warning);
        }

        if (warning is not null)
        {
            return RoutingResult.Ok(_response, message: warning, level: GH_RuntimeMessageLevel.Warning);
        }

        return DescribeUsage() is { } usageNote
            ? RoutingResult.Ok(_response, message: usageNote, level: GH_RuntimeMessageLevel.Remark)
            : RoutingResult.Ok(_response);
    }

    /// <summary>
    /// Validates the outgoing conversation's tool pairing and repairs it when it is broken, so a
    /// defect upstream costs a warning and a slightly shorter prompt instead of a rejected request
    /// and a halted loop. Sets <see cref="_repairWarning"/> when it had to change anything.
    /// </summary>
    /// <param name="data">The Instructions carried by the consumed signal.</param>
    /// <returns>The original Instructions when valid; a repaired copy otherwise.</returns>
    private Instructions RepairToolPairing(Instructions data)
    {
        IReadOnlyList<string> problems = ToolPairing.FindProblems(data.Conversation);
        if (problems.Count == 0)
        {
            return data;
        }

        Conversation repaired = CompactionInvariants.Reassemble(data.Conversation.Messages);
        int droppedTurns = data.Conversation.Count - repaired.Count;

        string listed = string.Join("; ", problems.Take(MaxReportedPairingProblems));
        if (problems.Count > MaxReportedPairingProblems)
        {
            listed += $"; and {problems.Count - MaxReportedPairingProblems} more";
        }

        _repairWarning =
            "The conversation handed to this component had broken tool pairing, which every provider "
            + $"rejects outright: {listed}. It was repaired before sending"
            + (droppedTurns > 0 ? $" ({droppedTurns} turn(s) removed)" : string.Empty)
            + ", so this round still ran. Fix the cause upstream — a compaction window that cuts a "
            + "tool exchange in half is the usual one.";

        return new Instructions(data.SystemPrompt, repaired) { Tools = data.Tools };
    }

    /// <summary>
    /// Combines two canvas warnings into one message, keeping the earlier one first.
    /// </summary>
    /// <param name="existing">The warning accumulated so far, or null.</param>
    /// <param name="addition">The warning to add.</param>
    /// <returns>The combined message.</returns>
    private static string AppendWarning(string? existing, string addition) =>
        existing is null ? addition : existing + " " + addition;

    /// <summary>
    /// Renders the last call's token usage as a one-line remark, naming the cached share when the
    /// provider reported one.
    /// </summary>
    /// <returns>The remark, or null when the provider reported no usage.</returns>
    private string? DescribeUsage()
    {
        if (_usage is not { } usage)
        {
            return null;
        }

        // Cached tokens are NOT included in InputTokens, so the prompt total has to add them back —
        // reporting InputTokens alone makes a cache hit look like the prompt shrank.
        int prompt = usage.InputTokens + usage.CacheWriteTokens + usage.CacheReadTokens;
        string note = $"{prompt:N0} prompt / {usage.OutputTokens:N0} output tokens";

        if (usage.CacheReadTokens > 0)
        {
            note += $" — {usage.CacheReadTokens:N0} read from cache ({usage.CacheReadTokens * 100 / Math.Max(prompt, 1)}% of the prompt)";
        }
        else if (usage.CacheWriteTokens > 0)
        {
            note += $" — {usage.CacheWriteTokens:N0} written to cache (the next turn should read them back)";
        }

        return note;
    }

    private void StartInference(Instructions instructions, ModelConfig config, IReadOnlyList<LlmToolDefinition> tools)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _isRunning = true;

        // The try/catch must cover the ENTIRE body: an uncaught throw here is an
        // unobserved task exception — the read pass never fires and the component
        // hangs Active with no message.
        Task.Run(async () =>
        {
            try
            {
                var provider = LlmProviderFactory.GetProvider(config);

                if (provider == null)
                {
                    _apiError = $"No provider registered for config type '{config.GetType().Name}'.";
                    _isRunning = false;
                    RequestReadPass();
                    return;
                }

                var sb = new StringBuilder();
                lock (_streamLock)
                {
                    _streamBuffer = sb;
                }

                string? error = null;
                LlmErrorKind? errorKind = null;
                bool success = true;
                IReadOnlyList<LlmToolCall>? toolCalls = null;

                await foreach (var chunk in provider.StreamAsync(
                    instructions.Conversation,
                    instructions.SystemPrompt,
                    config,
                    tools.Count > 0 ? tools : null,
                    ct))
                {
                    if (chunk.IsOk(out var value, out var chunkError))
                    {
                        if (value.ContentDelta != null)
                        {
                            // Guarded: the Chat window reads this buffer from the UI thread mid-stream.
                            lock (_streamLock)
                            {
                                sb.Append(value.ContentDelta);
                            }
                        }

                        // Tool calls arrive on the final chunk; keep the last non-empty set.
                        if (value.ToolCalls is { Count: > 0 } chunkCalls)
                        {
                            toolCalls = chunkCalls;
                        }

                        // Stop reason arrives on the final chunk; published to the read pass
                        // like _response/_apiError.
                        if (value.StopReason != null)
                        {
                            _stopReason = value.StopReason;
                        }

                        // Usage rides the final chunk; keep the last non-null set.
                        if (value.Usage != null)
                        {
                            _usage = value.Usage;
                        }
                    }
                    else
                    {
                        if (chunkError.Kind != LlmErrorKind.Cancelled)
                        {
                            error = chunkError.Message;
                            errorKind = chunkError.Kind;
                        }

                        success = false;
                        break;
                    }
                }

                _isRunning = false;

                if (ct.IsCancellationRequested)
                {
                    // Cancelled — the OnSolveTick cancel handler already aborted the read pass.
                    return;
                }

                if (success)
                {
                    _response = sb.ToString();
                    _toolCalls = toolCalls;
                    _apiError = null;
                    _apiErrorKind = null;
                }
                else
                {
                    _apiError = error ?? "The LLM API returned an error.";
                    _apiErrorKind = errorKind;
                }

                RequestReadPass();
            }
            catch (Exception ex)
            {
                _isRunning = false;

                if (ct.IsCancellationRequested)
                {
                    // Cancelled — the OnSolveTick cancel handler already aborted the read pass.
                    return;
                }

                _apiError = ex.Message;
                RequestReadPass();
            }
        }, ct);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tears down any warm CLI session this component owns (Claude Code, Codex) so its subprocess
    /// does not outlive the component on the canvas. Both pools are keyed on this instance GUID,
    /// and ending a key the pool never held is a no-op, so both are ended unconditionally.
    /// </remarks>
    public override void RemovedFromDocument(GH_Document document)
    {
        _cts?.Cancel();
        ClaudeCodeProvider.EndSession(InstanceGuid);
        CodexProvider.EndSession(InstanceGuid);
        base.RemovedFromDocument(document);
    }
}
