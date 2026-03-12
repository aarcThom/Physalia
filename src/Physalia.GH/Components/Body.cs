// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.Core.Parsing;
using Physalia.GH.Attributes;
using Physalia.GH.Helpers;

namespace Physalia.GH.Components
{
    /// <summary>
    /// The BODY component receives a generated script from BRAIN, rebuilds its dynamic
    /// parameters, and executes the script against the current Grasshopper inputs.
    /// </summary>
    public class Body : GH_Component, IGH_VariableParameterComponent
    {
        private readonly ScriptRunner _scriptRunner = new ();
        private ScriptResponse? _lastResponse;

        /// <summary>
        /// Initializes a new instance of the <see cref="Body"/> class.
        /// </summary>
        public Body()
            : base("BODY", "BODY", "Description", "Physalia", "Core")
        {
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("EB60CC78-D8DE-4C8C-8FC7-ADE7FFD18E4C"); }
        }

        /// <summary>
        /// Gets the icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap? Icon => null;

        /// <summary>
        /// Stores the latest LLM response, rebuilds the component's dynamic parameters,
        /// and schedules a solution refresh.
        /// </summary>
        /// <param name="response">The parsed LLM response containing the script and parameter definitions.</param>
        public void ReceiveResponse(ScriptResponse response)
        {
            _lastResponse = response;
            ParameterBuilder.Rebuild(this, response, 0, 0);
            ExpireSolution(true);
        }

        /// <summary>
        /// Opens the Eto.Forms script editor dialog for the currently stored script.
        /// </summary>
        public void OpenScriptEditor()
        {
            if (_lastResponse == null)
            {
                return;
            }

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

        /// <summary>
        /// Assigns the custom <see cref="BodyAttrib"/> attribute class to this component.
        /// </summary>
        public override void CreateAttributes() => m_attributes = new BodyAttrib(this);

        /// <summary>
        /// Gets a value indicating whether a parameter can be inserted at the given index on the specified side.
        /// </summary>
        /// <param name="side">The parameter side (input or output).</param>
        /// <param name="index">The index at which the parameter would be inserted.</param>
        /// <returns>false — dynamic parameters are managed by BRAIN; manual insertion is not permitted.</returns>
        public bool CanInsertParameter(GH_ParameterSide side, int index) => false;

        /// <summary>
        /// Gets a value indicating whether a parameter can be removed at the given index on the specified side.
        /// </summary>
        /// <param name="side">The parameter side (input or output).</param>
        /// <param name="index">The index of the parameter to remove.</param>
        /// <returns>false — dynamic parameters are managed by BRAIN; manual removal is not permitted.</returns>
        public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;

        /// <summary>
        /// Creates a default placeholder parameter for the given side and index.
        /// </summary>
        /// <param name="side">The parameter side (input or output).</param>
        /// <param name="index">The index at which the parameter will be placed.</param>
        /// <returns>A new generic object parameter.</returns>
        public IGH_Param CreateParameter(GH_ParameterSide side, int index) => new Param_GenericObject();

        /// <summary>
        /// Called when a parameter is removed; performs any necessary cleanup.
        /// </summary>
        /// <param name="side">The parameter side (input or output).</param>
        /// <param name="index">The index of the parameter being removed.</param>
        /// <returns>true.</returns>
        public bool DestroyParameter(GH_ParameterSide side, int index) => true;

        /// <summary>
        /// Called after parameters are added or removed; performs any required maintenance.
        /// </summary>
        public void VariableParameterMaintenance()
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        /// <param name="pManager">The GH_InputParamManager for registering input parameters.</param>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
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
            if (_lastResponse == null)
            {
                return;
            }

            try
            {
                _scriptRunner.Execute(DA, _lastResponse, 0, 0);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Script execution failed: {ex.Message}");
            }
        }
    }
}
