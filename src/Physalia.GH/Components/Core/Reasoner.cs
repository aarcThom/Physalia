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
using Physalia.Core.Config;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Core inference component. Receives Instructions from Recorder and performs a single
/// forward pass, streaming the result. Stateless between calls — all context lives in Recorder.
/// </summary>
public class Reasoner : PhyBase
{
    private string _response = string.Empty;
    private IReadOnlyList<LlmToolCall>? _toolCalls;
    private bool _hasNewResult;
    private bool _isRunning;
    private bool _lastTrigger;
    private bool _lastCancel;
    private CancellationTokenSource? _cts;
    private string? _errorWarning;

    /// <summary>
    /// Initializes a new instance of the <see cref="Reasoner"/> class.
    /// </summary>
    public Reasoner()
        : base("Reasoner", "Rea", "Performs a single LLM forward pass and streams the response.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F1097B2B-564A-43F8-8F70-BA6961F00E00");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "instructions", "I", "Conversation history and system prompt from Recorder.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_ModelConfig(), "model", "M", "Model configuration from an Anthropic Model or Tweaker component.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("trigger", "T", "Boolean trigger from Recorder. Rising edge starts inference.", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("cancel", "X", "Boolean button. Rising edge cancels the active inference call.", GH_ParamAccess.item, false);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("response", "R", "Raw LLM response text.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_LlmToolCall(), "tool calls", "TC", "Tool calls requested by the model. Null when the response contains only text.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("trigger", "T", "Fires true once when inference completes.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_errorWarning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _errorWarning);
            _errorWarning = null;
        }

        var instructionsGoo = new GH_Instructions();
        var modelGoo = new GH_ModelConfig();
        bool trigger = false;
        bool cancel = false;

        if (!DA.GetData(0, ref instructionsGoo)) return;
        if (!DA.GetData(1, ref modelGoo)) return;
        DA.GetData(2, ref trigger);
        DA.GetData(3, ref cancel);

        if (cancel && !_lastCancel)
        {
            _cts?.Cancel();
            _isRunning = false;
        }

        if (trigger && !_lastTrigger && !_isRunning
            && instructionsGoo.Value is Instructions instructions
            && modelGoo.Value is ModelConfig config)
        {
            _response = string.Empty;
            _toolCalls = null;
            _hasNewResult = false;
            StartInference(instructions, config);
        }

        _lastTrigger = trigger;
        _lastCancel = cancel;

        if (_isRunning)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Inference running…");
        }

        bool triggerOut = _hasNewResult;
        var toolCallsOut = _hasNewResult ? _toolCalls : null;
        _hasNewResult = false;

        DA.SetData(0, _response);

        if (toolCallsOut != null)
        {
            DA.SetDataList(1, toolCallsOut.Select(tc => new GH_LlmToolCall(tc)));
        }

        DA.SetData(2, triggerOut);
    }

    private void StartInference(Instructions instructions, ModelConfig config)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _isRunning = true;

        Task.Run(async () =>
        {
            var provider = LlmProviderFactory.GetProvider(config);

            if (provider == null)
            {
                _errorWarning = $"No provider registered for config type '{config.GetType().Name}'.";
                _isRunning = false;
                OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
                return;
            }

            var sb = new StringBuilder();
            IReadOnlyList<LlmToolCall>? toolCalls = null;
            bool success = true;

            await foreach (var chunk in provider.StreamAsync(
                instructions.Conversation,
                instructions.SystemPrompt,
                config,
                ct))
            {
                if (chunk is Result<LlmResponseChunk, LlmError>.Ok ok)
                {
                    if (ok.Value.ContentDelta != null)
                    {
                        sb.Append(ok.Value.ContentDelta);
                    }

                    if (ok.Value.ToolCalls != null)
                    {
                        toolCalls = ok.Value.ToolCalls;
                    }
                }
                else if (chunk is Result<LlmResponseChunk, LlmError>.Err err)
                {
                    if (err.Error.Kind != LlmErrorKind.Cancelled)
                    {
                        _errorWarning = err.Error.Message;
                    }

                    success = false;
                    break;
                }
            }

            _isRunning = false;

            if (success && !ct.IsCancellationRequested)
            {
                _response = sb.ToString();
                _toolCalls = toolCalls;
                _hasNewResult = true;
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }, ct);
    }
}
