// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.Core.Providers;
using Physalia.GH.ParamTypes;
using SiteReader.Components;

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// A base class for all the provider componenets.
    /// </summary>
    public abstract class ProviderBase : PhyBase
    {
        /// <summary>
        /// The GUID for the inheriting component.
        /// </summary>
        protected string _guidString;

        /// <summary>
        /// The output LlmProvider object.
        /// </summary>
        protected LlmProvider _llmProvider;

        /// <summary>
        /// The provider common name.
        /// </summary>
        private protected string _providerName;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProviderBase"/> class.
        /// The LlmProvider base class. Used for the input provider components.
        /// </summary>
        /// <param name="name">Component full name.</param>
        /// <param name="nickname">Component Nickname.</param>
        /// <param name="description">Descripton of the component.</param>
        /// <param name="guidString">A GUID for the inheriting component.</param>
        /// <param name="providerName">The provider name. Must match the entry in the API keys JSON.</param>
        public ProviderBase(string name, string nickname, string description, string guidString, string providerName)
          : base(name, nickname, description, "LLM Providers")
        {
            _guidString = guidString;
            _providerName = providerName;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // none needed for these components!
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new LlmProviderGhParam(), "Provider", "Pvdr", "A connection to a LLM provider's API.", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            try
            {
                var apiKeyResolver = new ApiKeyResolver();
                var apiKey = apiKeyResolver.GetKey(_providerName);

                _llmProvider = LlmProviderFactory.Create(_providerName, apiKey);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
            }

            DA.SetData(0, new LlmProviderGoo(_llmProvider));
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid(_guidString); }
        }
    }
}