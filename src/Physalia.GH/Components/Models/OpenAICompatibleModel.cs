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
            "Configures a connection to any OpenAI-compatible endpoint: OpenAI, OpenRouter, DeepSeek, Groq, Ollama, or llama.cpp.",
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
        pManager.AddTextParameter("Base URL", "U", "Base URL of the API endpoint. Default: https://api.openai.com/v1", GH_ParamAccess.item, "https://api.openai.com/v1");
        pManager.AddParameter(new Param_ApiKey(), "API Key", "K", "API key for authentication. Leave empty for local servers that do not require a key.", GH_ParamAccess.item);
        pManager[1].Optional = true;
        pManager.AddTextParameter("Model", "M", "Model ID, e.g. gpt-4o or anthropic/claude-sonnet-4-6. Wire a Picker component to select from the endpoint's available models.", GH_ParamAccess.item, string.Empty);
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
        string model = string.Empty;
        int maxTokens = 4096;

        GH_ApiKey keyGoo = null;
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

            if (result is Result<IReadOnlyList<string>, LlmError>.Ok ok)
            {
                SetValues("Model", ok.Value);
            }
            else if (result is Result<IReadOnlyList<string>, LlmError>.Err err)
            {
                _fetchWarning = $"Could not reach endpoint: {err.Error.Message}";
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
