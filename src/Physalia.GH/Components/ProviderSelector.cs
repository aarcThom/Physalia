// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.GH.Attributes;
using Physalia.GH.ParamTypes;

namespace Physalia.GH.Components;

/// <summary>
/// Component to select the LLM provider.
/// </summary>
public class ProviderSelector : PhyBase
{
    /// <summary>
    /// The LLM provider selected by the user.
    /// </summary>
    public string SelectedProvider;

    /// <summary>
    /// The list of providers with valid API keys.
    /// </summary>
    public List<string> AvailableProviders;

    private readonly ApiKeyResolver _apiKeyResolver; // used to get the api keys, list available providers

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderSelector"/> class.
    /// </summary>
    public ProviderSelector()
      : base("Provider", "Pvdr", "The LLM provider from which you will select a model. Right click to set API key if needed.", "Core")
    {
        // get the available providers
        _apiKeyResolver = new ApiKeyResolver();
    }

    /// <summary>
    /// Gets the unique ID for this component. Do not change this ID after release.
    /// </summary>
    public override Guid ComponentGuid
    {
        get { return new Guid("DE62B84D-8648-4F39-BC02-1FCB2B8AA304"); }
    }

    /// <summary>
    /// Assigns the custom <see cref="ProviderSelectorAttrib"/> attribute class to this component.
    /// </summary>
    public override void CreateAttributes() => m_attributes = new ProviderSelectorAttrib(this);

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Registers all the output parameters for this component.
    /// </summary>
    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new LlmProviderGhParam(), "Provider", "Pvdr", "The LLM provider from which you will select a model.", GH_ParamAccess.item);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        AvailableProviders = _apiKeyResolver.GetAvailableProviders();

        if (SelectedProvider != null)
        {
            var apiKey = _apiKeyResolver.GetKey(SelectedProvider);
            var provider = LlmProviderFactory.Create(SelectedProvider, apiKey);
            DA.SetData(0, new LlmProviderGoo(provider));
        }
    }
}
