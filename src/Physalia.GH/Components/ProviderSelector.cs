// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.Core.Providers;
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
        IconPath = "Physalia.GH.Resources.provider.png";

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
    /// Serializes the selected provider name so the choice survives save and reload.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("SelectedProvider", SelectedProvider ?? string.Empty);
        return base.Write(writer);
    }

    /// <summary>
    /// Deserialises the selected provider name.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IReader reader)
    {
        string value = string.Empty;
        if (reader.TryGetString("SelectedProvider", ref value) && !string.IsNullOrEmpty(value))
        {
            SelectedProvider = value;
        }

        return base.Read(reader);
    }

    /// <summary>
    /// Adding the right click menu button to allow user to open their APIkeys file.
    /// </summary>
    /// <param name="menu">GH menu.</param>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(menu, "Edit API keys file", OpenKeys);
    }

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

    /// <summary>
    /// Opens the API keys file so the user can edit it.
    /// </summary>
    /// <param name="sender">GH default.</param>
    /// <param name="e">GH default event.</param>
    private void OpenKeys(object sender, EventArgs e)
    {
        var path = ApiKeyResolver.KeyFilePath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("notepad.exe", path);
        }
        else
        {
            Process.Start("open", $"-e \"{path}\"");
        }
    }
}
