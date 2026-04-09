// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Providers;
using Physalia.GH.Attributes;
using Physalia.GH.ParamTypes;

namespace Physalia.GH.Components;

/// <summary>
/// The ModelSelector component resolves API keys, lets the user select a provider and model
/// via inline dropdowns, and outputs a configured <see cref="LlmProvider"/> to COMPOSER.
/// </summary>
public class ModelSelector : PhyBase
{
    private string _fetchWarning;
    private LlmProvider _llmProvider;
    private LlmProvider _lastProvider;
    private Task? _pendingModelFetch;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelSelector"/> class.
    /// </summary>
    public ModelSelector()
        : base("Model", "LLM", "The LLM that you want to work with.", "Core")
    {
    }

    /// <summary>
    /// Gets or sets the list of model IDs available for the currently selected provider.
    /// </summary>
    public List<string> AvailableModels { get; set; } = new ();

    /// <summary>
    /// Gets or sets the model ID chosen by the user in the dropdown.
    /// </summary>
    public string SelectedModel { get; set; } = string.Empty;

    /// <summary>
    /// Gets the unique ID for this component. Do not change this ID after release.
    /// </summary>
    public override Guid ComponentGuid
    {
        get { return new Guid("7FF05B85-EF6E-4DC4-8826-DEEB6C065CA5"); }
    }

    /// <summary>
    /// Assigns the custom <see cref="ModelSelectorAttrib"/> attribute class to this component.
    /// </summary>
    public override void CreateAttributes() => m_attributes = new ModelSelectorAttrib(this);

    /// <summary>
    /// Serializes the selected model ID so the choice survives save and reload.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("SelectedModel", SelectedModel ?? string.Empty);
        return base.Write(writer);
    }

    /// <summary>
    /// Deserialises the selected model ID. The model list is re-fetched from the provider
    /// on the next solve; the restored selection is applied once a provider is available.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IReader reader)
    {
        string value = string.Empty;
        if (reader.TryGetString("SelectedModel", ref value) && !string.IsNullOrEmpty(value))
        {
            SelectedModel = value;
        }

        return base.Read(reader);
    }

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new LlmProviderGhParam(), "Provider", "Pvdr", "LLM Provider", GH_ParamAccess.item);
    }

    /// <summary>
    /// Registers all the output parameters for this component.
    /// </summary>
    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new LlmProviderGhParam(), "LLM", "LLM", "The LLM used to interact with the GH canvas.", GH_ParamAccess.item);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // needed if the last model fetch returns an error or no models
        if (!string.IsNullOrEmpty(_fetchWarning))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _fetchWarning);
            _fetchWarning = null;
        }

        LlmProviderGoo? llmGoo = null;

        if (!DA.GetData(0, ref llmGoo))
        {
            return;
        }

        LlmProvider incoming = llmGoo.Value; // unwrap the container

        // need to check if provider has been changed
        bool providerChanged = _lastProvider != null && !ReferenceEquals(incoming, _lastProvider);
        if (providerChanged)
        {
            SelectedModel = string.Empty;
            AvailableModels = new List<string>();
            _pendingModelFetch = null; // force re-fetch
            _lastProvider = incoming;
        }

        _llmProvider = incoming;

        // Check if the data we got is actually null DA.GetData returns true if ANYTHING is passed in.
        if (llmGoo == null || llmGoo.Value == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "You haven't provided a valid LLM provider.");
            return;
        }

        if (_pendingModelFetch == null || _pendingModelFetch.IsCompleted)
        {
            _pendingModelFetch = FetchModelsAsync();
        }

        if (!string.IsNullOrEmpty(SelectedModel))
        {
            _llmProvider.CurrentModel = SelectedModel; // set the model to user selection before sending to composer
            DA.SetData(0, new LlmProviderGoo(_llmProvider));
        }
    }

    private async Task FetchModelsAsync()
    {
        try
        {
            await _llmProvider.GetModelsAsync();

            // I don't think this would ever happen, but just in case....
            if (_llmProvider.Models.Count == 0)
            {
                _fetchWarning = "Your API key seems to be correct, but the provider is not returning any models.";
                return;
            }

            AvailableModels = _llmProvider.Models.ToList();

            ExpireSolution(true); // re-render so model dropdown reflects new list
        }
        catch (Exception ex)
        {
            AvailableModels = new List<string>();
            _fetchWarning = $"Could not fetch models: {ex.Message}";
            ExpireSolution(true);
        }
    }
}
