using Grasshopper.Kernel;
using Physalia.GH.ParamTypes;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Physalia.GH.Components.Providers
{
    public abstract class ProviderBase : GH_Component
    {
        protected string _guidString;

        /// <summary>
        /// Initializes a new instance of the ProviderBase class.
        /// </summary>
        public ProviderBase(string name, string nickname, string description, string guidString)
          : base(name, nickname, description, "Physalia", "LLM Providers")
        {
            _guidString = guidString;
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

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid(_guidString); }
        }
    }
}