using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.Core.Providers;
using Physalia.GH.Attributes;
using Physalia.GH.Goo;
using Physalia.GH.ParamTypes;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Physalia.GH.Components
{
    public class Dream : GH_Component
    {
        private  ApiKeyResolver _apiKeyResolver;

        public List<string> AvailableProviders { get; set; } = new();
        public List<string> AvailableModels { get; set; } = new();
        public string SelectedProvider { get; set; } = "";
        public string SelectedModel { get; set; } = "";

        private readonly AnthropicProvider _anthropicProvider = new();
        private Task? _pendingModelFetch;
        private string _lastFetchedProvider = "";

        /// <summary>
        /// Initializes a new instance of the Dream class.
        /// </summary>
        public Dream()
          : base("Dream", "Nickname",
              "Description",
              "Physalia", "Core")
        {
            string apiKeysPath = "C:/test.json";
            _apiKeyResolver = new ApiKeyResolver(apiKeysPath);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Param_LlmConfig(), "Config", "Cfg", "Provider, model, and API key", GH_ParamAccess.item);
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
                var key = _apiKeyResolver.GetKey(SelectedProvider);
                var config = new LlmConfig { Provider = SelectedProvider, ModelId = SelectedModel, ApiKey = key };
                DA.SetData(0, new GH_LlmConfig(config));
            }
        }

        private async Task FetchModelsAsync(string provider)
        {
            try
            {
                var apiKey = _apiKeyResolver.GetKey(provider);
                // for now only anthropic is supported
                var models = await _anthropicProvider.GetModelsAsync(apiKey);
                AvailableModels = models;
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
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

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