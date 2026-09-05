// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.GH.Config;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Emits one configured provider's endpoint and key as a single Model API value.
/// </summary>
/// <remarks>
/// <para>Replaces the old API Keys component. Its output carries the endpoint as well as the key,
/// which is what let the OpenAI-compatible node drop its Base URL input: the two always travelled
/// together, and only one of them was ever on the wire.</para>
/// <para>Providers are configured in the chat window ("Configure LLM providers"), which writes them
/// to an encrypted store. Nothing is read from or written to a plain-text file by this component.</para>
/// </remarks>
public class ModelApiComponent : PhyBase, IPickableValuesSource
{
    private List<string> _availableProviders = new();
    private string _lastProviderList = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelApiComponent"/> class.
    /// </summary>
    public ModelApiComponent()
        : base("Model API", "API", "Hands one provider's endpoint and key to a Model component. Set providers up in the chat window; the key itself never appears on the canvas and is never written into your .gh file.", "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("9F14B7C2-3D68-4E05-8A71-B62E9C4D07F5");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput("Provider", this._availableProviders) };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        if (inputName == "Provider")
            this._availableProviders = new List<string>(values);
    }

    /// <inheritdoc/>
    public void ResetValues() => this._availableProviders.Clear();

    /// <summary>
    /// When dropped onto the canvas, auto-place a Picker wired to the provider input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (GhJsonBridge.IsImporting) return;

        if (this.Params.Input[0].SourceCount > 0) return;

        ComponentHelpers.PickerAdd(this, document, 0);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Provider", "P", "Which provider to use. Wire the Picker placed alongside — it lists everything you have set up in the chat window.", GH_ParamAccess.item, string.Empty);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelApi(), "Model API", "API", "That provider's endpoint and key. Wire it into a Model component; neither is ever shown, printed or saved.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Re-resolve every solve so a provider configured in the chat window shows up without a
        // reload. The store caches its decrypt, so this is cheap.
        IReadOnlyList<ModelApi> configured = PhyCredentials.Resolver.All();

        // Repopulate the Picker only when the provider set has actually changed.
        string providerList = string.Join(",", configured.Select(a => a.Provider));
        if (providerList != this._lastProviderList)
        {
            this._lastProviderList = providerList;
            this.SetValues("Provider", configured.Select(a => a.Provider));

            this.OnPingDocument()?.ScheduleSolution(1, _ =>
            {
                foreach (var source in this.Params.Input[0].Sources)
                    (source.Attributes?.GetTopLevel?.DocObject as IGH_ActiveObject)?.ExpireSolution(false);
                this.ExpireSolution(true);
            });
        }

        // A store that exists but cannot be decrypted is NOT the same as an empty one, and saying
        // "nothing configured" here would send the user off to re-enter keys they already have.
        string? unreadable = PhyCredentials.Resolver.UnreadableReason;
        if (unreadable is not null)
        {
            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, unreadable);
            return;
        }

        if (configured.Count == 0)
        {
            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No providers are set up yet. Open the chat window and choose \"Configure LLM providers\".");
            return;
        }

        string provider = string.Empty;
        DA.GetData(0, ref provider);

        if (string.IsNullOrWhiteSpace(provider))
        {
            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No provider selected. Wire a Picker to choose one.");
            return;
        }

        ModelApi? match = configured.FirstOrDefault(a =>
            string.Equals(a.Provider, provider, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"\"{provider}\" is not set up. Configure it in the chat window, or pick one of: {string.Join(", ", configured.Select(a => a.Provider))}.");
            return;
        }

        DA.SetData(0, new GH_ModelApi(match));
    }
}
