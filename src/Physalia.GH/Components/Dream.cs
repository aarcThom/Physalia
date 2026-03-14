// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.Core.Providers;
using Physalia.GH.Attributes;
using Physalia.GH.ParamTypes;

namespace Physalia.GH.Components
{
    /// <summary>
    /// The DREAM component resolves API keys, lets the user select a provider and model
    /// via inline dropdowns, and outputs a configured <see cref="LlmProvider"/> to BRAIN.
    /// </summary>
    public class Dream : GH_Component
    {
        private ApiKeyResolver _apiKeyResolver;

        /// <summary>
        /// Gets or sets the list of providers that have a valid API key configured.
        /// </summary>
        public List<string> AvailableProviders { get; set; } = new ();

        /// <summary>
        /// Gets or sets the list of model IDs available for the currently selected provider.
        /// </summary>
        public List<string> AvailableModels { get; set; } = new ();

        /// <summary>
        /// Gets or sets the provider name chosen by the user in the dropdown.
        /// </summary>
        public string SelectedProvider { get; set; } = "";

        /// <summary>
        /// Gets or sets the model ID chosen by the user in the dropdown.
        /// </summary>
        public string SelectedModel { get; set; } = "";

        private LlmProvider _llmProvider;
        private Task? _pendingModelFetch;
        private string _lastFetchedProvider = "";

        /// <summary>
        /// Initializes a new instance of the <see cref="Dream"/> class.
        /// </summary>
        public Dream()
          : base("Dream", "Nickname",
              "Description",
              "Physalia", "Core")
        {
            _apiKeyResolver = new ApiKeyResolver();
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new LlmProviderGhParam(), "Config", "Cfg", "Provider, model, and API key", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            AvailableProviders = _apiKeyResolver.GetAvailableProviders();

            if (SelectedProvider != _lastFetchedProvider && !string.IsNullOrEmpty(SelectedProvider)
                && (_pendingModelFetch == null || _pendingModelFetch.IsCompleted))
            {
                _pendingModelFetch = FetchModelsAsync(SelectedProvider);
            }

            if (!string.IsNullOrEmpty(SelectedProvider) && !string.IsNullOrEmpty(SelectedModel))
            {
                _llmProvider.CurrentModel = SelectedModel; // set the model to user selection before sending to Brain
                DA.SetData(0, new LlmProviderGoo(_llmProvider));
            }
        }

        private async Task FetchModelsAsync(string provider)
        {
            try
            {
                var apiKey = _apiKeyResolver.GetKey(provider);

                _llmProvider = LlmProviderFactory.Create(provider, apiKey);
                await _llmProvider.GetModelsAsync();

                AvailableModels = _llmProvider.Models.ToList();
                _lastFetchedProvider = provider;
                ExpireSolution(true); // re-render so model dropdown reflects new list
            }
            catch (Exception ex)
            {
                AvailableModels = new List<string>();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not fetch models: {ex.Message}");
                ExpireSolution(true);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Assigns the custom <see cref="DreamAttrib"/> attribute class to this component.
        /// </summary>
        public override void CreateAttributes() => m_attributes = new DreamAttrib(this);

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("7FF05B85-EF6E-4DC4-8826-DEEB6C065CA5"); }
        }
    }
}
