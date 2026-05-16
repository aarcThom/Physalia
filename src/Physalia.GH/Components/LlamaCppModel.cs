// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Config;
using Physalia.Core.Models.Named;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Grasshopper component that configures a connection to a local llama.cpp server.
/// Queries the server for the loaded model name when the URL changes.
/// </summary>
public class LlamaCppModel : PhyBase
{
    private string _lastBaseUrl = string.Empty;
    private string? _loadedModel;
    private string? _fetchWarning;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaCppModel"/> class.
    /// </summary>
    public LlamaCppModel()
        : base("LlamaCpp Model", "Llama", "Configures a connection to a local llama.cpp server.", "Config")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3A4B5C6D-7E8F-9A0B-C1D2-E3F4A5B6C7D8");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("base url", "U", "Base URL of the llama-server. Default: http://127.0.0.1:8080/v1", GH_ParamAccess.item, "http://127.0.0.1:8080/v1");
        pManager.AddTextParameter("api key", "k", "API key. Leave empty unless the server was started with --api-key.", GH_ParamAccess.item, string.Empty);
        pManager.AddIntegerParameter("max tokens", "T", "Maximum number of tokens to generate.", GH_ParamAccess.item, 2048);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "model", "M", "Configured llama.cpp model.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_fetchWarning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _fetchWarning);
            _fetchWarning = null;
        }

        string baseUrl = "http://127.0.0.1:8080/v1";
        string apiKey = string.Empty;
        int maxTokens = 2048;

        DA.GetData(0, ref baseUrl);
        DA.GetData(1, ref apiKey);
        DA.GetData(2, ref maxTokens);

        if (!string.IsNullOrWhiteSpace(baseUrl) && baseUrl != _lastBaseUrl)
        {
            _lastBaseUrl = baseUrl;
            _loadedModel = null;
            StartModelFetch(baseUrl, apiKey);
        }

        if (_loadedModel != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Loaded: {_loadedModel}");
        }

        var config = new LlamaCppConfig(
            ModelId: _loadedModel ?? "local",
            ApiKey: apiKey,
            MaxTokens: maxTokens,
            BaseUrl: baseUrl);

        DA.SetData(0, new GH_ModelConfig(config));
    }

    private void StartModelFetch(string baseUrl, string apiKey)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            var config = new LlamaCppConfig(BaseUrl: baseUrl, ApiKey: apiKey);
            var provider = LlmProviderFactory.GetProvider(config)!;
            var result = await provider.GetAvailableModelsAsync(config, ct);

            if (ct.IsCancellationRequested) return;

            if (result is Result<IReadOnlyList<string>, LlmError>.Ok ok && ok.Value.Count > 0)
            {
                _loadedModel = ok.Value[0];
            }
            else if (result is Result<IReadOnlyList<string>, LlmError>.Err err)
            {
                _fetchWarning = $"Could not reach server: {err.Error.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }, ct);
    }
}
