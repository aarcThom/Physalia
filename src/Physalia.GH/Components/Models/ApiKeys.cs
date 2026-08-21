// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Reads <c>API_KEY_CONFIG.YAML</c> from the plugin Files folder and outputs the
/// API key for the selected provider.
/// </summary>
public class ApiKeys : PhyBase, IPickableValuesSource
{
    private IReadOnlyList<ApiKey> _keys = Array.Empty<ApiKey>();
    private List<string> _availableProviders = new();
    private string _lastProviderList = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeys"/> class.
    /// </summary>
    public ApiKeys()
        : base("API Keys", "APIKeys", "Fetches the key for one provider out of API_KEY_CONFIG.YAML. The secret itself never appears on the canvas and is never written into your .gh file.", "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6066E324-4A4D-4C08-B72B-5268ED7DE772");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput("Provider", _availableProviders) };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        if (inputName == "Provider")
            _availableProviders = new List<string>(values);
    }

    /// <inheritdoc/>
    public void ResetValues() => _availableProviders.Clear();

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Provider", "P", "Whose key to fetch. Wire the Picker placed alongside — it lists the sections found in the YAML file.", GH_ParamAccess.item, string.Empty);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ApiKey(), "API Key", "K", "The key for that provider, carried as a label only. Wire it into a Model component; the secret is never shown, printed or saved.", GH_ParamAccess.item);
    }

    /// <summary>
    /// When dropped onto the canvas, auto-place a Picker wired to the provider input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (GhJsonBridge.IsImporting) return;

        if (Params.Input[0].SourceCount > 0) return;

        ComponentHelpers.PickerAdd(this, document, 0);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Re-read the config on every solve so changes to the file are picked up.
        _keys = Api.GetKeys(GetConfigFilePath());

        // Repopulate the picker only when the provider set has changed.
        string providerList = string.Join(",", _keys.Select(k => k.Provider));
        if (providerList != _lastProviderList)
        {
            _lastProviderList = providerList;
            SetValues("Provider", _keys.Select(k => k.Provider));

            OnPingDocument()?.ScheduleSolution(1, _ =>
            {
                foreach (var source in Params.Input[0].Sources)
                    (source.Attributes?.GetTopLevel?.DocObject as IGH_ActiveObject)?.ExpireSolution(false);
                ExpireSolution(true);
            });
        }

        if (_keys.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No API keys found. Check that API_KEY_CONFIG.YAML exists in the plugin Files folder.");
            return;
        }

        string provider = string.Empty;
        DA.GetData(0, ref provider);

        var match = _keys.FirstOrDefault(k =>
            string.Equals(k.Provider, provider, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"No key found for provider \"{provider}\".");
            return;
        }

        DA.SetData(0, new GH_ApiKey(match));
    }

    private static string GetConfigFilePath()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return assemblyDir is null
            ? "API_KEY_CONFIG.YAML"
            : Path.Combine(assemblyDir, "Files", "API_KEY_CONFIG.YAML");
    }
}
