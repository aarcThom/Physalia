// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Config;
using Physalia.Core.Models;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base for Model components that take an API key, fetch the provider's available
/// model list asynchronously, expose it to a Picker via <see cref="IPickableValuesSource"/>,
/// and emit a configured <see cref="ModelConfig"/>. Subclasses supply only their
/// identity strings and the concrete config record.
/// </summary>
public abstract class ModelComponentBase : PhyBase, IPickableValuesSource
{
    private string _lastApiKey = string.Empty;
    private List<string> _availableModels = new();
    private string? _fetchWarning;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelComponentBase"/> class.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected ModelComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, "Models")
    {
    }

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput("Model", _availableModels) };

    /// <summary>
    /// Gets the description for the API Key input parameter.
    /// </summary>
    protected abstract string ApiKeyDescription { get; }

    /// <summary>
    /// Gets the description for the configured Model output parameter.
    /// </summary>
    protected abstract string ModelOutputDescription { get; }

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        if (inputName == "Model")
            _availableModels = new List<string>(values);
    }

    /// <inheritdoc/>
    public void ResetValues() => _availableModels.Clear();

    /// <summary>
    /// When dropped onto the canvas, auto-place a Picker wired to the model input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (GhJsonBridge.IsImporting) return;

        if (Params.Input[1].SourceCount > 0) return;

        ComponentHelpers.PickerAdd(this, document, 1);
    }

    /// <summary>
    /// Creates the concrete provider config for the given model and key. Called with an
    /// empty model ID when building the throwaway config used for the model-list fetch.
    /// </summary>
    /// <param name="modelId">The selected model ID, or empty for a fetch config.</param>
    /// <param name="apiKey">The API key.</param>
    /// <returns>The configured provider config record.</returns>
    protected abstract ModelConfig CreateConfig(string modelId, string apiKey);

    /// <inheritdoc/>
    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ApiKey(), "API Key", "K", ApiKeyDescription, GH_ParamAccess.item);
        pManager[0].Optional = true;
        pManager.AddTextParameter("Model", "M", "Model ID. Wire a Picker component to select from available models.", GH_ParamAccess.item, string.Empty);
        RegisterAdditionalInputs(pManager);
    }

    /// <summary>
    /// Registers subclass-specific inputs after the shared API Key and Model inputs
    /// (i.e. starting at index 2). Appending here keeps saved-document param layouts
    /// stable. Default registers nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Applies subclass-specific inputs (registered via <see cref="RegisterAdditionalInputs"/>)
    /// to the freshly created config. Default returns the config unchanged.
    /// </summary>
    /// <param name="config">The config created by <see cref="CreateConfig"/>.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>The adjusted config.</returns>
    protected virtual ModelConfig ApplyAdditionalInputs(ModelConfig config, IGH_DataAccess da) => config;

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", ModelOutputDescription, GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        if (_fetchWarning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _fetchWarning);
            _fetchWarning = null;
        }

        GH_ApiKey? keyGoo = null;
        DA.GetData(0, ref keyGoo);
        string apiKey = keyGoo?.Value?.Key ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(apiKey) && apiKey != _lastApiKey)
        {
            _lastApiKey = apiKey;
            ResetValues();
            StartModelFetch(apiKey);
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

        DA.SetData(0, new GH_ModelConfig(ApplyAdditionalInputs(CreateConfig(modelId, apiKey), DA)));
    }

    private void StartModelFetch(string apiKey)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            var config = CreateConfig(string.Empty, apiKey);
            var provider = LlmProviderFactory.GetProvider(config)!;
            var result = await provider.GetAvailableModelsAsync(config, ct);

            if (ct.IsCancellationRequested) return;

            if (result.IsOk(out var models, out var err))
            {
                SetValues("Model", models);
            }
            else
            {
                _fetchWarning = $"Failed to fetch models: {err.Message}";
            }

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
