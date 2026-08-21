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
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Configures a connection to any OpenAI-compatible endpoint,
/// including OpenAI, OpenRouter, DeepSeek, Groq, Ollama, and llama.cpp.
/// When Model is empty the first model reported by the endpoint is used automatically.
/// </summary>
public class OpenAICompatibleModel : PhyBase, IPickableValuesSource
{
    private string _lastFetchKey = string.Empty;
    private List<string> _availableModels = new();
    private string? _fetchWarning;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAICompatibleModel"/> class.
    /// </summary>
    public OpenAICompatibleModel()
        : base(
            "OpenAI Compatible Model",
            "OAIModel",
            "Points the pipeline at anything that speaks the OpenAI API — OpenAI itself, OpenRouter, DeepSeek, Groq, Ollama, a local llama.cpp server. Changing the endpoint is a matter of changing the URL.",
            "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C4D5E6F7-A8B9-4C0D-E1F2-A3B4C5D6E7F8");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput("Model", _availableModels) };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        if (inputName == "Model")
            _availableModels = new List<string>(values);
    }

    /// <inheritdoc/>
    public void ResetValues() => _availableModels.Clear();

    /// <summary>
    /// When dropped onto the canvas, auto-place a Picker wired to the Model input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (GhJsonBridge.IsImporting) return;
        if (Params.Input[2].SourceCount > 0) return;

        ComponentHelpers.PickerAdd(this, document, 2);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Base URL", "U", "Where the endpoint lives. Left as https://api.openai.com/v1 it is OpenAI; swap it for OpenRouter, DeepSeek, or a server running on your own machine.", GH_ParamAccess.item, "https://api.openai.com/v1");
        pManager.AddParameter(new Param_ApiKey(), "API Key", "K", "The key for that endpoint. Wire an API Keys component, or leave it empty for a local server that asks for none.", GH_ParamAccess.item);
        pManager[1].Optional = true;
        pManager.AddTextParameter("Model", "M", "Which model to use — gpt-4o, or a prefixed name like anthropic/claude-sonnet-4-6 on OpenRouter. The Picker placed alongside lists what this endpoint offers.", GH_ParamAccess.item, string.Empty);
        pManager.AddIntegerParameter("Max Tokens", "T", "The ceiling on one reply. Raise it if answers come back cut off mid-sentence.", GH_ParamAccess.item, 4096);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "The model, configured. Wire into an LLM Call, or through an OpenAI Compatible Tweaker first to change how it samples.", GH_ParamAccess.item);
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
        string model = string.Empty;
        int maxTokens = 4096;

        GH_ApiKey? keyGoo = null;
        DA.GetData(0, ref baseUrl);
        DA.GetData(1, ref keyGoo);
        DA.GetData(2, ref model);
        DA.GetData(3, ref maxTokens);

        string apiKey = keyGoo?.Value?.Key ?? string.Empty;

        string fetchKey = baseUrl + "||" + apiKey;

        if (!string.IsNullOrWhiteSpace(baseUrl) && fetchKey != _lastFetchKey)
        {
            _lastFetchKey = fetchKey;
            ResetValues();
            StartModelFetch(baseUrl, apiKey);
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No model selected. Wire a Picker to choose from the endpoint's available models.");
            return;
        }

        var config = new OpenAICompatibleConfig(
            ModelId: model,
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

            if (result.IsOk(out var models, out var err))
            {
                SetValues("Model", models);
            }
            else
            {
                _fetchWarning = $"Could not reach endpoint: {err.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ =>
            {
                foreach (var source in Params.Input[2].Sources)
                {
                    (source.Attributes?.GetTopLevel?.DocObject as IGH_ActiveObject)?.ExpireSolution(false);
                }

                ExpireSolution(true);
            });
        }, ct);
    }
}
