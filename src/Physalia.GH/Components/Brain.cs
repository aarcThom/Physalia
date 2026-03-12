// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Parsing;
using Physalia.Core.Prompts;
using Physalia.Core.Providers;
using Physalia.GH.Attributes;
using Physalia.GH.ParamTypes;

namespace Physalia.GH.Components
{
    /// <summary>
    /// The BRAIN component receives an LLM provider from DREAM, accepts a user prompt,
    /// calls the LLM API asynchronously, and forwards the parsed response to a linked BODY component.
    /// </summary>
    public class Brain : GH_Component
    {
        private Task? _pendingRequest;
        private string? _errorMsg;
        private string? _lastPrompt;

        /// <summary>
        /// Initializes a new instance of the <see cref="Brain"/> class.
        /// </summary>
        public Brain()
            : base("BRAIN", "BRAIN", "Description", "Physalia", "Core")
        {
        }

        /// <summary>
        /// Gets or sets the BODY component that receives the LLM-generated script.
        /// </summary>
        public Body? BodyComponent { get; set; }

        /// <summary>
        /// Gets or sets the instance GUID of the linked BODY component, used for serialization and reconnection.
        /// </summary>
        public Guid BodyGuid { get; set; }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("B904F3D0-72CC-4B43-A15E-497CE8478638"); }
        }

        /// <summary>
        /// Gets the icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap? Icon => null;

        /// <summary>
        /// Assigns the custom <see cref="BrainAttrib"/> attribute class to this component.
        /// </summary>
        public override void CreateAttributes() => m_attributes = new BrainAttrib(this);

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        /// <param name="pManager">The GH_InputParamManager for registering input parameters.</param>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddParameter(new LlmProviderGhParam(), "Llm", "Llm", "Large language model from DREAM", GH_ParamAccess.item);
            pManager.AddTextParameter("Prompt", "Prmpt", "The prompt", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Send", "Snd", "Send Prompt to defined model", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        /// <param name="pManager">The GH_OutputParamManager for registering output parameters.</param>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            LlmProviderGoo? llmGoo = null;
            string? prompt = null;
            bool send = false;

            if (!DA.GetData(0, ref llmGoo))
            {
                return;
            }

            if (!DA.GetData(1, ref prompt))
            {
                return;
            }

            if (!DA.GetData(2, ref send))
            {
                return;
            }

            if (_errorMsg != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _errorMsg);
                _errorMsg = null;
            }

            if (BodyComponent == null)
            {
                return; // no body linked yet
            }

            if (send && (_pendingRequest == null || _pendingRequest.IsCompleted) && prompt != _lastPrompt)
            {
                Message = "Calling API...";

                var llm = llmGoo.Value;
                _pendingRequest = SendRequestAsync(prompt, llm, BodyComponent);
            }
        }

        private async Task SendRequestAsync(string prompt, LlmProvider llModel, Body body)
        {
            try
            {
                var rawResult = await llModel.SendPromptAsync(SystemPrompt.Default, prompt);
                var formattedResult = ResponseParser.Parse(rawResult);
                _lastPrompt = prompt;
                body.ReceiveResponse(formattedResult);  // Body handles rebuild + expire
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
    }
}
