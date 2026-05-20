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
/// Configures a connection to any OpenAI-compatible endpoint,
/// including OpenAI, OpenRouter, DeepSeek, Groq, Ollama, and llama.cpp.
/// When Model is empty the first model reported by the endpoint is used automatically.
/// </summary>
public class OpenAICompatibleModel : PhyBase
{
    private string _lastFetchKey = string.Empty;
    private string? _fetchedModel;
    private string? _fetchWarning;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAICompatibleModel"/> class.
    /// </summary>
    public OpenAICompatibleModel()
        : base(
            "OpenAI Compatible Model",
            "OAIModel",
            "Configures a connection to any OpenAI-compatible endpoint: OpenAI, OpenRouter, DeepSeek, Groq, Ollama, or llama.cpp.",
            "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C4D5E6F7-A8B9-4C0D-E1F2-A3B4C5D6E7F8");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Base URL", "U", "Base URL of the API endpoint. Default: https://api.openai.com/v1", GH_ParamAccess.item, "https://api.openai.com/v1");
        pManager.AddTextParameter("API Key", "K", "API key for authentication. Leave empty for local servers that do not require a key.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Model", "M", "Model identifier, e.g. gpt-4o or anthropic/claude-sonnet-4-6. Leave empty to auto-detect from the endpoint.", GH_ParamAccess.item, string.Empty);
        pManager.AddIntegerParameter("Max Tokens", "T", "Maximum number of tokens to generate.", GH_ParamAccess.item, 4096);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Configured OpenAI-compatible model.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_fetchWarning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _fetchWarning);
            _fetchWarning = null;
        }

        string baseUrl = "https://api.openai.com/v1";
        string apiKey = string.Empty;
        string model = string.Empty;
        int maxTokens = 4096;

        DA.GetData(0, ref baseUrl);
        DA.GetData(1, ref apiKey);
        DA.GetData(2, ref model);
        DA.GetData(3, ref maxTokens);

        bool useAutoModel = string.IsNullOrWhiteSpace(model);
        string fetchKey = baseUrl + "||" + apiKey;

        if (useAutoModel && fetchKey != _lastFetchKey)
        {
            _lastFetchKey = fetchKey;
            _fetchedModel = null;
            StartModelFetch(baseUrl, apiKey);
        }

        string modelId = useAutoModel ? (_fetchedModel ?? string.Empty) : model;

        if (useAutoModel && _fetchedModel != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Loaded: {_fetchedModel}");
        }

        var config = new OpenAICompatibleConfig(
            ModelId: modelId,
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
            var config = new OpenAICompatibleConfig(BaseUrl: baseUrl, ApiKey: apiKey);
            var provider = LlmProviderFactory.GetProvider(config)!;
            var result = await provider.GetAvailableModelsAsync(config, ct);

            if (ct.IsCancellationRequested) return;

            if (result is Result<IReadOnlyList<string>, LlmError>.Ok ok && ok.Value.Count > 0)
            {
                _fetchedModel = ok.Value[0];
            }
            else if (result is Result<IReadOnlyList<string>, LlmError>.Err err)
            {
                _fetchWarning = $"Could not reach endpoint: {err.Error.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }, ct);
    }
}
