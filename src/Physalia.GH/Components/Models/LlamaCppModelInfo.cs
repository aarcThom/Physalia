// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Models;
using Physalia.Core.Models.Protocol;
using Physalia.Core.Tokens;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Queries a running llama-server for context window size, and looks up capability flags
/// in the LiteLLM model database using the GGUF-normalised model ID.
/// Context window is sourced from the server (<c>GET /v1/models</c> then <c>GET /props</c>).
/// Image Capable and Tool Calls are sourced from the LiteLLM database.
/// </summary>
public class LlamaCppModelInfo : PhyBase
{
    private static readonly HttpClient _modelsHttpClient = new HttpClient();
    private static IReadOnlyDictionary<string, ModelEntry>? _models;
    private static bool _isFetching;

    private readonly HttpClient _httpClient = new HttpClient();

    private string _lastKey = string.Empty;
    private LlamaCppServerProps? _lastResult;
    private string? _warning;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaCppModelInfo"/> class.
    /// </summary>
    public LlamaCppModelInfo()
        : base(
            "LlamaCpp Model Info",
            "LCppInfo",
            "Queries a running llama-server for context window size. Capability flags are inferred from the LiteLLM database using the GGUF-normalised model ID.",
            "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B8670B74-3727-4FF6-A0E3-46957060603B");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "OpenAI-compatible model configuration pointing to a llama-server instance.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Max Input", "I", "Training context length (n_ctx_train) from the server — the architectural maximum from the GGUF header.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Max Output", "O", "Server context window (n_ctx). May be less than Max Input if --ctx-size was set at launch.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Image Capable", "V", "Whether the model accepts image inputs. Inferred from the LiteLLM database using the GGUF-normalised model ID. False if the model is not found.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Tool Calls", "T", "Whether the model supports function/tool calling. Inferred from the LiteLLM database using the GGUF-normalised model ID. False if the model is not found.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_warning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _warning);
            _warning = null;
        }

        if (_models == null && !_isFetching)
        {
            _isFetching = true;
            StartModelsFetch();
        }

        var goo = new GH_ModelConfig();
        if (!DA.GetData(0, ref goo)) return;

        if (goo.Value is not OpenAIProtocolConfig config)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input must be an OpenAI-compatible model configuration.");
            return;
        }

        string key = config.BaseUrl + "||" + config.ApiKey;

        if (key != _lastKey)
        {
            _lastKey = key;
            _lastResult = null;
            StartQuery(config);
        }

        bool vision = false;
        bool toolCalls = false;

        if (_models != null)
        {
            var entry = ModelList.Find(_models, config.ModelId);
            if (entry != null)
            {
                vision = entry.SupportsVision;
                toolCalls = entry.SupportsToolCalls;
            }
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Loading model database...");
        }

        if (_lastResult != null)
        {
            DA.SetData(0, _lastResult.NCtxTrain);
            DA.SetData(1, _lastResult.NCtx);
        }
        else
        {
            DA.SetData(0, 0);
            DA.SetData(1, 0);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Querying server...");
        }

        DA.SetData(2, vision);
        DA.SetData(3, toolCalls);
    }

    private void StartQuery(OpenAIProtocolConfig config)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            var result = await LlamaCppServerQuery.GetPropsAsync(config, _httpClient, ct);

            if (ct.IsCancellationRequested) return;

            if (result is Result<LlamaCppServerProps, LlmError>.Ok ok)
            {
                _lastResult = ok.Value;
            }
            else if (result is Result<LlamaCppServerProps, LlmError>.Err err)
            {
                _warning = $"Could not retrieve server props: {err.Error.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }, ct);
    }

    private void StartModelsFetch()
    {
        Task.Run(async () =>
        {
            var result = await ModelList.FetchAsync(_modelsHttpClient);

            if (result is Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Ok ok)
            {
                _models = ok.Value;
            }
            else
            {
                _isFetching = false;
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        });
    }
}
