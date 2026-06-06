// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
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
/// forward pass, streaming the result. Stateless between calls — all context lives in
/// Recorder. Routes the response forward (Data + Success Trigger) or an API error back
/// (Feedback + Fail Trigger) through <see cref="RoutingComponentBase{TData}"/>.
/// </summary>
public class Reasoner : RoutingComponentBase<Instructions>
{
    private int _cancelIndex = -1;
    private bool _lastCancel;
    private bool _isRunning;
    private string _response = string.Empty;
    private string? _apiError;
    private CancellationTokenSource? _cts;

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
    protected override bool AutoScheduleRead => false;

    /// <inheritdoc/>
    protected override void RegisterDataInput(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Instructions(), "Instructions", "I", "Conversation history and system prompt from Recorder.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Model configuration from a Model or Tweaker component.", GH_ParamAccess.item);
        _cancelIndex = pManager.AddBooleanParameter("Cancel", "X", "Rising edge cancels the active inference call.", GH_ParamAccess.item, false);
    }

    /// <inheritdoc/>
    protected override bool TryGetData(IGH_DataAccess da, out Instructions data)
    {
        data = default!;
        var goo = new GH_Instructions();
        if (!da.GetData(0, ref goo) || goo.Value is not Instructions instructions)
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

        var modelGoo = new GH_ModelConfig();
        if (!da.GetData(1, ref modelGoo) || modelGoo.Value is not ModelConfig config)
        {
            _apiError = "No valid Model configuration connected.";
            RequestReadPass();
            return;
        }

        StartInference(data, config);
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(Instructions data, IGH_DataAccess da)
    {
        return _apiError != null
            ? RoutingResult.Fail(_apiError, _apiError, GH_RuntimeMessageLevel.Error)
            : RoutingResult.Ok(_response);
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
                _apiError = $"No provider registered for config type '{config.GetType().Name}'.";
                _isRunning = false;
                RequestReadPass();
                return;
            }

            var sb = new StringBuilder();
            string? error = null;
            bool success = true;

            try
            {
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
            }
            catch (Exception ex)
            {
                error = ex.Message;
                success = false;
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
                _apiError = null;
            }
            else
            {
                _apiError = error ?? "The LLM API returned an error.";
            }

            RequestReadPass();
        }, ct);
    }
}
