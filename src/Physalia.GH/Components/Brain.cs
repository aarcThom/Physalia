using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.Core.Providers;
using Physalia.GH.Attributes;
using Physalia.GH.Goo;
using Physalia.GH.Helpers;
using Physalia.GH.ParamTypes;
using System;
using System.Threading.Tasks;


namespace Physalia.GH.Components
{
    public class Brain : GH_Component
    {
        private readonly ApiCaller _apiCaller = new ApiCaller(new AnthropicProviderSS()); // need to remove hardcoding
        private Task? _pendingRequest;
        private string? _errorMsg;
        private string? _lastPrompt;


        public Body BodyComponent { get; set;} // the body to reference
        public Guid BodyGuid { get; set;} // the body's GUID

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public Brain()
          : base("BRAIN", "BRAIN",
              "Description",
              "Physalia", "Core")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Param_LlmConfig(), "Config", "Cfg", "LLM config from DREAM", GH_ParamAccess.item);
            pManager.AddTextParameter("Prompt", "Prmpt", "The prompt", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Send", "Snd", "Send Prompt to defined model", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_LlmConfig llmGoo = null;
            string prompt = null;
            bool send = false;

            if (!DA.GetData(0, ref llmGoo)) return;
            var config = llmGoo.Value;

            if (!DA.GetData(1, ref prompt)) return;
            if (!DA.GetData(2, ref send)) return;

            if (_errorMsg != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _errorMsg);
                _errorMsg = null;
            }

            if (BodyComponent == null) return; // no body linked yet

            if (send && (_pendingRequest == null || _pendingRequest.IsCompleted) && prompt != _lastPrompt)
            {
                Message = "Calling API...";
                _pendingRequest = SendRequestAsync(prompt, config, BodyComponent);
            }

        }

        private async Task SendRequestAsync(string prompt, LlmConfig llmConfig, Body body)
        {
            try
            {
                var result = await _apiCaller.SendAsync(prompt, llmConfig);
                _lastPrompt = prompt;
                body.ReceiveResponse(result);  // Body handles rebuild + expire
                Message = "Done";
                ExpireSolution(true);
            }
            catch (Exception ex)
            {
                _errorMsg = ex.InnerException?.Message ?? ex.Message;
                Message = "Error";
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

        // set the attributes to the custom BrainAttrib
        public override void CreateAttributes() => m_attributes = new BrainAttrib(this);



        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("B904F3D0-72CC-4B43-A15E-497CE8478638"); }
        }
    }
}