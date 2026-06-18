// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Config;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.Core.Providers.ClaudeCode;
using Physalia.Core.Signals;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Core inference component. Receives Instructions from Recorder and performs a single
/// forward pass, streaming the result. Stateless between calls — all context lives in
/// Recorder. Routes the response forward on the Success Signal or an API error back on
/// the Fail Signal through <see cref="RoutingComponentBase{TData}"/>.
/// </summary>
public class Reasoner : RoutingComponentBase<Instructions>, IStreamingTextSource
{
    private int _cancelIndex = -1;
    private int _toolsIndex = -1;
    private bool _lastCancel;
    private bool _isRunning;
    private string _response = string.Empty;
    private string? _apiError;
    private IReadOnlyList<LlmToolCall>? _toolCalls;
    private CancellationTokenSource? _cts;

    // Live streaming buffer: the response text accumulated so far this run. Appended on the
    // background inference thread and read by the Prompter on the UI thread, so every access is
    // guarded by the lock. Null between runs; the Prompter shows it only while this is IsBusy.
    private readonly object _streamLock = new();
    private StringBuilder? _streamBuffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Reasoner"/> class.
    /// </summary>
    public Reasoner()
        : base("Reasoner", "Rea", "Performs a single LLM forward pass and routes the response.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F1097B2B-564A-43F8-8F70-BA6961F00E00");

    /// <inheritdoc/>
    /// <remarks>
    /// The Prompter reads this while the component IsBusy to render the response as it streams.
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
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Model configuration from a Model or Tweaker component.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "Conversation history and system prompt from Recorder.", GH_ParamAccess.item);
        _toolsIndex = pManager.AddParameter(new Param_ToolDefinition(), "Tools", "T", "Tool definitions from tool nodes, advertised to the model. Optional; leave unwired for plain inference.", GH_ParamAccess.list);
        pManager[_toolsIndex].Optional = true;
        _cancelIndex = pManager.AddBooleanParameter("Cancel", "X", "Rising edge cancels the active inference call.", GH_ParamAccess.item, false);
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Tool Calls", "TC", "Latched signal minted when the model requests tool calls; its content blocks carry the assistant turn (text + tool_use). Wire to a Router. Empty when the model returns a final answer (which routes on Success instead).", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    /// <remarks>The Tool Calls output registered by <see cref="RegisterAdditionalOutputs"/> sits at index 2.</remarks>
    protected override int AuxOutputIndex => 2;

    /// <inheritdoc/>
    /// <remarks>
    /// The signal payload (Recorder's user text) is deliberately ignored — the full
    /// context arrives on the typed Instructions input; the signal just says "go".
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out Instructions data)
    {
        data = default!;
        var goo = new GH_Instructions();
        if (!da.GetData(1, ref goo) || goo.Value is not Instructions instructions)
        {
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

        if (cancel && !_lastCancel && _isRunning)
        {
            _cts?.Cancel();
            _isRunning = false;
            AbortReadPass();
        }

        _lastCancel = cancel;
    }

    /// <inheritdoc/>
    /// <remarks>Starts the async inference. The read pass fires from the completion callback, not a fixed delay.</remarks>
    protected override void PushSolve(Instructions data, IGH_DataAccess da)
    {
        _apiError = null;
        _response = string.Empty;
        _toolCalls = null;

        var modelGoo = new GH_ModelConfig();
        if (!da.GetData(0, ref modelGoo) || modelGoo.Value is not ModelConfig config)
        {
            _apiError = "No valid Model configuration connected.";
            RequestReadPass();
            return;
        }

        // Stamp this component's identity so stateful providers (Claude Code's warm-process
        // pool) can keep one long-lived session per Reasoner across forward passes.
        config = config with { SessionKey = InstanceGuid };

        // Read the wired tool definitions on the solve thread; the async call only uses the snapshot.
        var toolGoos = new List<GH_ToolDefinition>();
        da.GetDataList(_toolsIndex, toolGoos);
        var tools = toolGoos
            .Where(g => g?.Value is not null)
            .Select(g => g!.Value!)
            .ToList();

        StartInference(data, config, tools);
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(Instructions data, IGH_DataAccess da)
    {
        if (_apiError != null)
        {
            return RoutingResult.Fail(_apiError, _apiError, GH_RuntimeMessageLevel.Error);
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
            return RoutingResult.Aux(signal);
        }

        return RoutingResult.Ok(_response);
    }

    private void StartInference(Instructions instructions, ModelConfig config, IReadOnlyList<ToolDefinition> tools)
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
                bool success = true;
                IReadOnlyList<LlmToolCall>? toolCalls = null;

                await foreach (var chunk in provider.StreamAsync(
                    instructions.Conversation,
                    instructions.SystemPrompt,
                    config,
                    tools.Count > 0 ? tools : null,
                    ct))
                {
                    if (chunk is Result<LlmResponseChunk, LlmError>.Ok ok)
                    {
                        if (ok.Value.ContentDelta != null)
                        {
                            // Guarded: the Prompter reads this buffer from the UI thread mid-stream.
                            lock (_streamLock)
                            {
                                sb.Append(ok.Value.ContentDelta);
                            }
                        }

                        // Tool calls arrive on the final chunk; keep the last non-empty set.
                        if (ok.Value.ToolCalls is { Count: > 0 } chunkCalls)
                        {
                            toolCalls = chunkCalls;
                        }
                    }
                    else if (chunk is Result<LlmResponseChunk, LlmError>.Err err)
                    {
                        if (err.Error.Kind != LlmErrorKind.Cancelled)
                        {
                            error = err.Error.Message;
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
                }
                else
                {
                    _apiError = error ?? "The LLM API returned an error.";
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
    /// Tears down any warm Claude Code CLI session this component owns so its subprocess does not
    /// outlive the component on the canvas.
    /// </remarks>
    public override void RemovedFromDocument(GH_Document document)
    {
        _cts?.Cancel();
        ClaudeCodeProvider.EndSession(InstanceGuid);
        base.RemovedFromDocument(document);
    }
}
