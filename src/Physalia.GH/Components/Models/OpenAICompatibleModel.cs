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
/// Configures a connection to any OpenAI-compatible endpoint, including OpenAI, OpenRouter,
/// DeepSeek, Alibaba, Z.AI, Moonshot, Groq, Ollama, and llama.cpp.
/// </summary>
/// <remarks>
/// <para><b>The Base URL input is gone</b> (2026-09): the endpoint now arrives on the Model API
/// wire alongside the key, because the two were always one fact — an OpenAI key means nothing at
/// DeepSeek's endpoint, and Alibaba, Z.AI and Moonshot are OpenAI-compatible at three different
/// hosts. The ComponentGuid changed with it: dropping input 0 shifts every remaining index, so an
/// archived layout cannot be restored onto the new one, and a clean "component not found" beats a
/// silent mis-wire that puts a Picker on Max Tokens.</para>
/// </remarks>
public class OpenAICompatibleModel : PhyBase, IPickableValuesSource
{
    private string _lastFetchKey = string.Empty;
    private List<string> _availableModels = new();
    private bool _modelsSettled;
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
    public override Guid ComponentGuid => new Guid("5A3E9D41-7C28-4B06-9E5F-1D84C0B7A2E6");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput("Model", _availableModels, _modelsSettled) };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        if (inputName == "Model")
            _availableModels = new List<string>(values);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Clearing also marks the list unsettled: it is emptied because a fetch is about to replace
    /// it, and until that lands a wired Picker must keep the choice it was restored with.
    /// </remarks>
    public void ResetValues()
    {
        _availableModels.Clear();
        _modelsSettled = false;
    }

    /// <summary>
    /// When dropped onto the canvas, auto-place a Picker wired to the Model input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (GhJsonBridge.IsImporting) return;
        if (Params.Input[1].SourceCount > 0) return;

        ComponentHelpers.PickerAdd(this, document, 1);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelApi(), "Model API", "API", "Which endpoint to talk to and the key for it. Wire a Model API component; set the provider up in the chat window.", GH_ParamAccess.item);
        pManager[0].Optional = true;
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

        string model = string.Empty;
        int maxTokens = 4096;

        GH_ModelApi? apiGoo = null;
        DA.GetData(0, ref apiGoo);
        DA.GetData(1, ref model);
        DA.GetData(2, ref maxTokens);

        ModelApi? api = apiGoo?.Value;

        if (api is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire a Model API component to say which endpoint to talk to.");
            return;
        }

        string baseUrl = api.BaseUrlOr("https://api.openai.com/v1");
        string apiKey = api.Key;

        string fetchKey = baseUrl + "||" + apiKey;

        if (fetchKey != _lastFetchKey)
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

            // The list is now as good as it will get — on failure it stays empty, which offers a
            // Picker nothing to fall back on, so a restored pick survives an unreachable endpoint.
            _modelsSettled = true;

            OnPingDocument()?.ScheduleSolution(1, _ =>
            {
                foreach (var source in Params.Input[1].Sources)
                {
                    (source.Attributes?.GetTopLevel?.DocObject as IGH_ActiveObject)?.ExpireSolution(false);
                }

                ExpireSolution(true);
            });
        }, ct);
    }
}
