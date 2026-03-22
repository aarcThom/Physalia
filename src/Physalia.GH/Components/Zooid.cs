#pragma warning disable

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.Core.Config;
using Physalia.Core.Parsing;
using Physalia.GH.Attributes;
using Physalia.GH.Helpers;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.GH.Components;

public class Zooid : PhyBase, IGH_VariableParameterComponent
{
    private readonly ScriptRunner _scriptRunner = new();
    private ScriptResponse? _lastResponse;

    /// <summary>
    /// Initializes a new instance of the PyZooid class.
    /// </summary>
    public Zooid()
      : base(
            "PyZooid",
            "PyZ",
            "Generated Python Script",
            "Core")
    {
    }

    /// <summary>
    /// Stores the latest LLM response, rebuilds the component's dynamic parameters,
    /// and schedules a solution refresh.
    /// </summary>
    /// <param name="response">The parsed LLM response containing the script and parameter definitions.</param>
    public void ReceiveResponse(ScriptResponse response)
    {
        _lastResponse = response;
        ParamBuddy.Rebuild(this, response);
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
    /// Gets the unique ID for this component. Do not change this ID after release.
    /// </summary>
    public override Guid ComponentGuid
    {
        get { return new Guid("7CD743AB-E4FC-4396-A190-4559632C4848"); }
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
        if (_lastResponse == null)
        {
            return;
        }

        try
        {
            _scriptRunner.Execute(DA, _lastResponse);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Script execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Assigns the custom <see cref="ZooidAttrib"/> attribute class to this component.
    /// </summary>
    public override void CreateAttributes() => m_attributes = new ZooidAttrib(this);

    public bool CanInsertParameter(GH_ParameterSide side, int index)
    {
        return true;
    }

    public bool CanRemoveParameter(GH_ParameterSide side, int index)
    {
        return true;
    }

    public IGH_Param CreateParameter(GH_ParameterSide side, int index)
    {
        if (side == GH_ParameterSide.Input)
        {
            string nick = NextParamName("in", Params.Input.Select(p => p.NickName));
            return new Param_GenericObject { Name = nick, NickName = nick, Description = PhyConstants.DefaultParamDescription, Optional = true };
        }
        else
        {
            string nick = NextParamName("out", Params.Output.Select(p => p.NickName));
            return new Param_GenericObject { Name = nick, NickName = nick, Description = PhyConstants.DefaultParamDescription, Optional = true };
        }
    }

    public bool DestroyParameter(GH_ParameterSide side, int index)
    {
        return true;
    }

    public void VariableParameterMaintenance()
    {
    }

    private static string NextParamName(string prefix, IEnumerable<string> existingParams)
    {
        // check to see the next available param name
        for (int i = 0; ;  i++)
        {
            string candidate = $"{prefix}_{i}";
            if (!existingParams.Contains(candidate))
            {
                return candidate ;
            }
        }
    }
}
