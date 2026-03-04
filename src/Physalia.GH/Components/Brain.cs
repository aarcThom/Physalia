using Grasshopper.Kernel;
using Physalia.GH.Attributes;
using Rhino.Runtime.Code.Execution;
using System;


namespace Physalia.GH.Components
{
    public class Brain : GH_Component
    {

        public GH_Component BodyComponent { get; set;} // the body to reference
        private Guid _bodyGuid;

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
            pManager.AddTextParameter("Model", "Mdl", "The model chosen to send the prompt to.", GH_ParamAccess.item);
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