// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Physalia.Core.Common;
using Physalia.Core.Config;
using Physalia.Core.Models.Named;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Grasshopper component that configures a Google Gemini model.
/// Fetches available models from the API and populates a value list automatically.
/// </summary>
public class GeminiModel : PhyBase
{
    private string _lastApiKey = string.Empty;
    private IReadOnlyList<string>? _availableModels;
    private IReadOnlyList<string>? _populatedWith;
    private string? _fetchWarning;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiModel"/> class.
    /// </summary>
    public GeminiModel()
        : base("Gemini Model", "Gemini", "Configures a Google Gemini model and fetches available models from the API.", "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6E412A9E-99CF-4CAC-9323-35B417FDA875");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("API Key", "K", "Google API key.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Model", "M", "Model ID. Connect the auto-placed value list or wire any string.", GH_ParamAccess.item, string.Empty);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Configured Gemini model.", GH_ParamAccess.item);
    }

    /// <summary>
    /// When dropped onto the canvas, auto-place a value list wired to the model input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        if (Params.Input[1].SourceCount > 0) return;

        ComponentHelpers.ValueListAdd(this, document, 1, "Enter API key");
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_fetchWarning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _fetchWarning);
            _fetchWarning = null;
        }

        string apiKey = string.Empty;
        if (!DA.GetData(0, ref apiKey)) return;

        if (!string.IsNullOrWhiteSpace(apiKey) && apiKey != _lastApiKey)
        {
            _lastApiKey = apiKey;
            _availableModels = null;
            _populatedWith = null;
            StartModelFetch(apiKey);
        }

        if (_availableModels != null && !ReferenceEquals(_availableModels, _populatedWith))
        {
            var valueList = Params.Input[1].Sources.OfType<GH_ValueList>().FirstOrDefault();
            if (valueList != null)
            {
                var models = _availableModels;
                _populatedWith = models;

                OnPingDocument()?.ScheduleSolution(1, _ =>
                {
                    valueList.ListItems.Clear();
                    foreach (var id in models)
                    {
                        valueList.ListItems.Add(new GH_ValueListItem(id, $"\"{id}\""));
                    }

                    if (valueList.ListItems.Count > 0)
                    {
                        valueList.SelectItem(0);
                    }

                    valueList.ExpireSolution(true);
                });
            }
        }

        string modelId = string.Empty;
        DA.GetData(1, ref modelId);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "API key is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No model selected.");
            return;
        }

        var config = new GeminiConfig(ModelId: modelId, ApiKey: apiKey);
        DA.SetData(0, new GH_ModelConfig(config));
    }

    private void StartModelFetch(string apiKey)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            var config = new GeminiConfig(ModelId: string.Empty, ApiKey: apiKey);
            var provider = LlmProviderFactory.GetProvider(config)!;
            var result = await provider.GetAvailableModelsAsync(config, ct);

            if (ct.IsCancellationRequested) return;

            if (result is Result<IReadOnlyList<string>, LlmError>.Ok ok)
            {
                _availableModels = ok.Value;
            }
            else if (result is Result<IReadOnlyList<string>, LlmError>.Err err)
            {
                _fetchWarning = $"Failed to fetch models: {err.Error.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }, ct);
    }
}
