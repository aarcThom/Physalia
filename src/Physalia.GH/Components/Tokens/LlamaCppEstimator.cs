// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.Common;
using Physalia.Core.Models.Protocol;
using Physalia.Core.Tokens;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Counts tokens via the llama.cpp native <c>/tokenize</c> endpoint.
/// Makes a local API call to the running llama-server — not calculated in-process.
/// Returns the exact token count for the model currently loaded in the server.
/// Not available on cloud providers such as OpenAI or OpenRouter.
/// </summary>
public class LlamaCppEstimator : PhyBase
{
    private readonly HttpClient _httpClient = new HttpClient();

    private string _lastKey = string.Empty;
    private int? _lastResult;
    private string? _warning;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaCppEstimator"/> class.
    /// </summary>
    public LlamaCppEstimator()
        : base(
            "LlamaCpp Estimator",
            "LCppEst",
            "Counts tokens via the llama.cpp /tokenize endpoint. Makes a local API call to llama-server — not in-process. Exact for the model currently loaded in the server.",
            "Tokens")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("0BE08386-64A8-45AE-ABEA-60EDAA5B2402");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D", "Instructions, Conversation, or text string to count.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "OpenAI-compatible model configuration (must point to a server that exposes /tokenize).", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Tokens", "T", "Exact token count returned by the server /tokenize endpoint.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_warning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _warning);
            _warning = null;
        }

        IGH_Goo? goo = null;
        if (!DA.GetData(0, ref goo)) return;

        var configGoo = new GH_ModelConfig();
        if (!DA.GetData(1, ref configGoo)) return;

        if (configGoo.Value is not OpenAIProtocolConfig config)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Config input must be an OpenAI-compatible model configuration.");
            return;
        }

        if (!TokenInputHelper.TryResolve(goo, out var instructions))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input could not be resolved to Instructions, Conversation, or text.");
            return;
        }

        string key = TokenInputHelper.BuildDataKey(instructions) + "||" + config.BaseUrl + config.ApiKey;

        if (key != _lastKey)
        {
            _lastKey = key;
            _lastResult = null;
            StartCount(instructions, config);
        }

        if (_lastResult.HasValue)
        {
            DA.SetData(0, _lastResult.Value);
        }
        else
        {
            DA.SetData(0, 0);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Counting tokens via server...");
        }
    }

    private void StartCount(Core.ConvoInstruct.Instructions instructions, OpenAIProtocolConfig config)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            var result = await AsyncTokenEstimation.CountLlamaCppAsync(instructions, config, _httpClient, ct);

            if (ct.IsCancellationRequested) return;

            if (result is Result<int, LlmError>.Ok ok)
            {
                _lastResult = ok.Value;
            }
            else if (result is Result<int, LlmError>.Err err)
            {
                _warning = $"Token count failed: {err.Error.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }, ct);
    }
}
