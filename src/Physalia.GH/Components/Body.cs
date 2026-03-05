using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.Core.Prompts;
using Physalia.GH.Attributes;
using Physalia.GH.Helpers;
using System;
using System.Linq;

namespace Physalia.GH.Components
{
    
    public class Body : GH_Component, IGH_VariableParameterComponent
    {
        private readonly ScriptRunner _scriptRunner = new ScriptRunner();
        private ScriptResponse? _lastResponse;


        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public Body()
          : base("BODY", "BODY",
              "Description",
              "Physalia", "Core")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
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
            if (_lastResponse == null) return;

            try
            {
                _scriptRunner.Execute(DA, _lastResponse, 0, 0);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Script execution failed: {ex.Message}");
            }
        }

        public void ReceiveResponse(ScriptResponse response)
        {
            _lastResponse = response;
            ParameterBuilder.Rebuild(this, response, 0, 0);
            ExpireSolution(true);
        }

        public void OpenScriptEditor()
        {
            if (_lastResponse == null) return;

            var inputNames = _lastResponse.Inputs.Select(i => i.Name).ToList();
            var dialog = new ScriptEditorDialog(_lastResponse.Script, inputNames);
            var result = dialog.ShowModal(Grasshopper.Instances.EtoDocumentEditor);

            if (result != null)
            {
                _lastResponse.Script = result;
                Message = "Edited";
                ExpireSolution(true);
            }
        }

        // set the attributes to the custom BodyAttrib
        public override void CreateAttributes() => m_attributes = new BodyAttrib(this);

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

        // --- IGH_VariableParameterComponent implementation ---

        public bool CanInsertParameter(GH_ParameterSide side, int index) => false;
        public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;
        public IGH_Param CreateParameter(GH_ParameterSide side, int index) => new Param_GenericObject();
        public bool DestroyParameter(GH_ParameterSide side, int index) => true;
        public void VariableParameterMaintenance() { }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("EB60CC78-D8DE-4C8C-8FC7-ADE7FFD18E4C"); }
        }
    }
}