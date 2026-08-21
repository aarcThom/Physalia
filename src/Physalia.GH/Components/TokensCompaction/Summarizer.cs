// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Compaction;
using Physalia.Core.Config;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.Core.Providers;
using Physalia.Core.Signals;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// LLM-backed compaction (the "Distiller"): replaces the older portion of a conversation with a
/// single model-written summary turn and keeps the most recent turns verbatim — the summary-buffer
/// pattern production agents use as their default. Unlike the deterministic windows it preserves the
/// meaning of dropped turns instead of forgetting them, at the cost of one inference call.
///
/// <para>An inline forward-path compactor (like the deterministic windows, but asynchronous): it
/// consumes a Conversation Log's Signal carrying the full Instructions, summarizes the older conversation, and
/// re-emits a Signal carrying the compacted Instructions on its single <b>Signal</b> output, wired straight
/// to the LLM Call (<c>Conversation Log → Summarizer → LLM Call</c>). The call runs asynchronously
/// (<see cref="AutoScheduleRead"/> is false; the read pass fires from the completion callback). The
/// system prompt is preserved (never summarized); only the conversation is.</para>
/// </summary>
public class Summarizer : RoutingComponentBase<Instructions>
{
    private const int InModel = 0;
    private const int InInstruction = 1;
    private const int InKeepRecent = 2;

    private CompactionResult? _result;
    private string? _error;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="Summarizer"/> class.
    /// </summary>
    public Summarizer()
        : base(
            "Summarizer",
            "Distill",
            "Shortens the conversation by having a model read the older part and write it up as a single turn, leaving the recent turns word for word. The only compactor that costs a call, and the only one that can keep what actually mattered.",
            "Tokens & Compaction")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("8241DBD1-BBE4-4A2D-B11B-8F1140859FBA");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The conversation to summarise, riding on a Conversation Log's signal. Usually reached from a Token Threshold's Over Limit output.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "The summarised conversation, ready for the LLM Call. If the summarising call fails, the conversation goes on in full — a longer prompt beats a lost turn.";

    /// <inheritdoc/>
    /// <remarks>Empty: this component has a single Signal output, so there is no Fail Signal to describe.</remarks>
    protected override string FailSignalDescription => string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// A single Signal output, matching <see cref="CompactionComponentBase"/>: compaction fails open,
    /// so nothing ever routes backwards and a Fail output would only duplicate the forward wire. See
    /// that class for why a failure signal could not have been wired anywhere useful.
    /// </remarks>
    protected override bool HasFailOutput => false;

    /// <inheritdoc/>
    protected override bool AutoScheduleRead => false;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Which model writes the summary. It need not be the one doing the real work — a cheaper, faster one is usually plenty.", GH_ParamAccess.item);
        int instructionIdx = pManager.AddTextParameter("Summary Prompt", "SP", "How to summarise — what must survive the squeeze. Left blank, a sensible default is used.", GH_ParamAccess.item, string.Empty);
        pManager.AddIntegerParameter("Keep Recent", "K", "How many of the most recent turns to leave exactly as they are. Everything older becomes the summary.", GH_ParamAccess.item, 6);
        pManager[instructionIdx].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>The Instructions to compact ride on the consumed signal itself (from the Conversation Log).</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out Instructions data)
    {
        data = default!;
        if (signal.Instructions is not Instructions instructions)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Signal carried no Instructions — wire a Conversation Log into this input.");
            return false;
        }

        data = instructions;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Starts the async summarization. The read pass fires from the completion callback.</remarks>
    protected override void PushSolve(Instructions data, IGH_DataAccess da)
    {
        _result = null;
        _error = null;

        var modelGoo = new GH_ModelConfig();
        if (!da.GetData(InModel, ref modelGoo) || modelGoo.Value is not ModelConfig config)
        {
            _error = "No valid Model configuration connected.";
            RequestReadPass();
            return;
        }

        string instruction = string.Empty;
        da.GetData(InInstruction, ref instruction);

        int keepRecent = 6;
        da.GetData(InKeepRecent, ref keepRecent);

        // Stamp this component's identity so a stateful provider (Claude Code's warm-process pool)
        // keeps one session per Summarizer rather than cold-starting each compaction.
        config = config with { SessionKey = InstanceGuid };

        // Compact only the conversation; the system prompt rides through the Conversation Log untouched.
        StartSummarization(data.Conversation, config, instruction, keepRecent);
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(Instructions data, IGH_DataAccess da)
    {
        // Fail open, exactly as the deterministic compactors do — and this is the one where it earns
        // its keep, because a summarization failure is a real runtime event (a network blip, a rate
        // limit) rather than a canvas mistake. Losing the turn to it would be absurd: the full
        // conversation is right here on the signal, so forward it unsummarized and say so.
        if (_error != null)
        {
            return PassThrough(data, _error);
        }

        if (_result is null)
        {
            return PassThrough(data, "the summarization call produced no result");
        }

        string trace = $"Compacted {_result.OriginalMessageCount} → {_result.RetainedMessageCount} messages";
        return RoutingResult.Ok(
            trace,
            // Carry the source tools forward unchanged — summarization shrinks the conversation, not
            // the set of tools advertised to the model, so the LLM Call still sees them past this node.
            instructions: new Instructions(data.SystemPrompt, _result.Conversation) { Tools = data.Tools },
            message: $"{trace} ({_result.DroppedMessageCount} folded into the summary).",
            level: GH_RuntimeMessageLevel.Remark);
    }

    /// <summary>
    /// The fail-open result: the ORIGINAL Instructions forwarded intact on the Signal output, so the
    /// LLM Call still receives its inference context when summarization could not run. The reason
    /// rides as a runtime message on the component rather than as signal payload — it is an operator
    /// problem, and putting it on the wire would land it in front of the model as a turn.
    /// </summary>
    /// <param name="source">The instructions that arrived, forwarded unchanged.</param>
    /// <param name="reason">Why summarization did not run.</param>
    /// <returns>A success <see cref="RoutingResult"/> carrying the unsummarized Instructions.</returns>
    private static RoutingResult PassThrough(Instructions source, string reason)
    {
        int count = source.Conversation.Count;
        return RoutingResult.Ok(
            $"Not summarized ({reason}); forwarded {count} message(s) unchanged",
            instructions: source,
            message: $"Summarization did not run — {reason}. The conversation was forwarded UNSUMMARIZED ({count} message(s)), so the prompt is longer than intended but the turn still runs.",
            level: GH_RuntimeMessageLevel.Error);
    }

    private void StartSummarization(Conversation conversation, ModelConfig config, string? instruction, int keepRecent)
    {
        ILlmProvider? provider = LlmProviderFactory.GetProvider(config);
        if (provider is null)
        {
            _error = $"No provider registered for config type '{config.GetType().Name}'.";
            RequestReadPass();
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        // The try/catch must cover the entire body: an uncaught throw is an unobserved task
        // exception — the read pass never fires and the component hangs Active with no message.
        Task.Run(async () =>
        {
            try
            {
                Result<CompactionResult, LlmError> result =
                    await ConversationSummarizer.SummarizeAsync(conversation, provider, config, instruction, keepRecent, ct);

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (result.IsOk(out CompactionResult? compaction, out LlmError? error))
                {
                    _result = compaction;
                }
                else
                {
                    _error = error.Message;
                }

                RequestReadPass();
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                _error = ex.Message;
                RequestReadPass();
            }
        }, ct);
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        _cts?.Cancel();
        base.RemovedFromDocument(document);
    }
}
