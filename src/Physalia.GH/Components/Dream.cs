// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Providers;
using Physalia.GH.Attributes;
using Physalia.GH.ParamTypes;
using SiteReader.Components;

namespace Physalia.GH.Components
{
    /// <summary>
    /// The DREAM component resolves API keys, lets the user select a provider and model
    /// via inline dropdowns, and outputs a configured <see cref="LlmProvider"/> to BRAIN.
    /// </summary>
    public class Dream : PhyBase
    {
        /// <summary>
        /// Gets or sets the list of model IDs available for the currently selected provider.
        /// </summary>
        public List<string> AvailableModels { get; set; } = new ();

        /// <summary>
        /// Gets or sets the model ID chosen by the user in the dropdown.
        /// </summary>
        public string SelectedModel { get; set; } = "";

        private LlmProvider _llmProvider;
        private Task? _pendingModelFetch;

        /// <summary>
        /// Initializes a new instance of the <see cref="Dream"/> class.
        /// </summary>
        public Dream()
            : base("Dream", "Nickname", "Description", "Core")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new LlmProviderGhParam(), "Provider", "Provider", "LLM Provider", GH_ParamAccess.item);
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

            LlmProviderGoo? llmGoo = null;

            if (!DA.GetData(0, ref llmGoo))
            {
                return;
            }

            _llmProvider = llmGoo.Value; // unwrap the container

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
                _llmProvider.CurrentModel = SelectedModel; // set the model to user selection before sending to Brain
                DA.SetData(0, new LlmProviderGoo(_llmProvider));
            }
        }

        private async Task FetchModelsAsync()
        {
            try
            {
                await _llmProvider.GetModelsAsync();

                AvailableModels = _llmProvider.Models.ToList();
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
