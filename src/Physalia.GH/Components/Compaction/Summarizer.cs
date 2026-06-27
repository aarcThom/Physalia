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
/// <para>Routes like the rest of the Compaction family (and like the Reasoner): a trigger
/// <c>Signal</c> starts the run, the source conversation arrives on the typed input, and the
/// compacted conversation is emitted on the <b>Success Signal</b>. The call runs asynchronously
/// (<see cref="AutoScheduleRead"/> is false; the read pass fires from the completion callback). Wire
/// the Success Signal through a <see cref="Feedback"/> → <see cref="FeedbackCollector"/> link back
/// to a Recorder's Conversation input, which breaks GH's acyclic constraint.</para>
/// </summary>
public class Summarizer : RoutingComponentBase<Instructions>
{
    private const int InSourceInstructions = 0;
    private const int InModel = 1;
    private const int InInstruction = 2;
    private const int InKeepRecent = 3;

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
            "Summarizes the older portion of a conversation into one turn and keeps recent turns verbatim. Uses an LLM call. Routes the compacted conversation on the Success Signal.",
            "Compaction")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("8241DBD1-BBE4-4A2D-B11B-8F1140859FBA");

    /// <inheritdoc/>
    protected override bool AutoScheduleRead => false;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "The instructions to compact — typically a Recorder's Instructions output. The system prompt is preserved (never summarized); only the conversation is.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Model configuration for the summarization call, from a Model or Tweaker component.", GH_ParamAccess.item);
        int instructionIdx = pManager.AddTextParameter("Summary Prompt", "SP", "Summarization instruction (system prompt for the compaction call). Optional; a sensible default is used when blank.", GH_ParamAccess.item, string.Empty);
        pManager.AddIntegerParameter("Keep Recent", "K", "How many of the most recent messages to keep verbatim; everything older is summarized into one turn.", GH_ParamAccess.item, 6);
        pManager[instructionIdx].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>The trigger signal just says "go"; the conversation to compact comes from the typed input.</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out Instructions data)
    {
        data = default!;
        var goo = new GH_Instructions();
        if (!da.GetData(InSourceInstructions, ref goo) || goo.Value is not Instructions instructions)
        {
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

        // Compact only the conversation; the system prompt rides through the Recorder untouched.
        StartSummarization(data.Conversation, config, instruction, keepRecent);
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(Instructions data, IGH_DataAccess da)
    {
        if (_error != null)
        {
            return RoutingResult.Fail(_error, _error, GH_RuntimeMessageLevel.Error);
        }

        if (_result is null)
        {
            return RoutingResult.Fail("Summarization produced no result.", "Summarization produced no result.", GH_RuntimeMessageLevel.Error);
        }

        string trace = $"Compacted {_result.OriginalMessageCount} → {_result.RetainedMessageCount} messages";
        return RoutingResult.Ok(
            trace,
            conversation: _result.Conversation,
            message: $"{trace} ({_result.DroppedMessageCount} folded into the summary).",
            level: GH_RuntimeMessageLevel.Remark);
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
