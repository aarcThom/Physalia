using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.Core.Prompts;
using Physalia.Core.Parsing;
using System;

namespace Physalia.GH.Components;

public class PhysaliaComponent : GH_Component, IGH_VariableParameterComponent
{
    // Store the last parsed response so we know what parameters we built
    private ScriptResponse? _lastResponse;

    public PhysaliaComponent()
        : base(
            "Physalia AI",
            "PhysAI",
            "Generate a Python 3 script from a natural language prompt",
            "Physalia",
            "AI")
    { }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // This is the only fixed input — always present
        pManager.AddTextParameter("JSON", "J", "JSON response from the LLM", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Build", "B", "Set to true to rebuild parameters", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // Fixed output: the script text (useful for debugging)
        pManager.AddTextParameter("Script", "S", "The parsed Python script", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string json = null;
        bool build = false;
        if (!DA.GetData(0, ref json)) return;
        if (!DA.GetData(1, ref build)) return;

        ScriptResponse result;
        try
        {
            result = ResponseParser.Parse(json);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        // Always output the script
        DA.SetData(0, result.Script);

        // Only rebuild parameters when Build is true
        if (build && !ResponseMatchesCurrent(result))
        {
            _lastResponse = result;
            RebuildParameters(result);
            return; // After rebuilding, we need a fresh solve with new params
        }
    }

    /// <summary>
    /// Checks if the parsed response matches the parameters we already have.
    /// </summary>
    private bool ResponseMatchesCurrent(ScriptResponse response)
    {
        if (_lastResponse == null) return false;

        // Simple check: same number of inputs and outputs with same names
        if (response.Inputs.Count != _lastResponse.Inputs.Count) return false;
        if (response.Outputs.Count != _lastResponse.Outputs.Count) return false;

        for (int i = 0; i < response.Inputs.Count; i++)
        {
            if (response.Inputs[i].Name != _lastResponse.Inputs[i].Name) return false;
        }
        for (int i = 0; i < response.Outputs.Count; i++)
        {
            if (response.Outputs[i].Name != _lastResponse.Outputs[i].Name) return false;
        }

        return true;
    }

    /// <summary>
    /// Removes old dynamic parameters and adds new ones based on the response.
    /// </summary>
    private void RebuildParameters(ScriptResponse response)
    {
        // --- Remove old dynamic inputs (everything after JSON and Build) ---
        while (Params.Input.Count > 2)
        {
            Params.UnregisterInputParameter(Params.Input[Params.Input.Count - 1]);
        }

        // --- Remove old dynamic outputs (everything after Script) ---
        while (Params.Output.Count > 1)
        {
            Params.UnregisterOutputParameter(Params.Output[Params.Output.Count - 1]);
        }

        // --- Add new inputs from the response ---
        foreach (var input in response.Inputs)
        {
            var param = CreateInputParam(input);
            Params.RegisterInputParam(param);
        }

        // --- Add new outputs from the response ---
        foreach (var output in response.Outputs)
        {
            var param = new Param_GenericObject
            {
                Name = output.PrettyName ?? output.Name,
                NickName = output.Name,
                Description = output.Tooltip ?? "",
                Access = GH_ParamAccess.item
            };
            Params.RegisterOutputParam(param);
        }

        Params.OnParametersChanged();
        ExpireSolution(true);
    }

    /// <summary>
    /// Creates a GH input parameter from a ParamDefinition.
    /// </summary>
    private static IGH_Param CreateInputParam(ParamDefinition def)
    {
        IGH_Param param = def.TypeHint?.ToLowerInvariant() switch
        {
            "int" => new Param_Integer(),
            "double" => new Param_Number(),
            "bool" => new Param_Boolean(),
            "string" => new Param_String(),
            "point3d" => new Param_Point(),
            "vector3d" => new Param_Vector(),
            "plane" => new Param_Plane(),
            "line" => new Param_Line(),
            "curve" => new Param_Curve(),
            "surface" => new Param_Surface(),
            "brep" => new Param_Brep(),
            "mesh" => new Param_Mesh(),
            "color" => new Param_Colour(),
            _ => new Param_GenericObject()
        };

        param.Name = def.PrettyName ?? def.Name;
        param.NickName = def.Name;
        param.Description = def.Tooltip ?? "";
        param.Optional = def.Optional;

        param.Access = def.Access?.ToLowerInvariant() switch
        {
            "list" => GH_ParamAccess.list,
            "tree" => GH_ParamAccess.tree,
            _ => GH_ParamAccess.item
        };

        return param;
    }

    // --- IGH_VariableParameterComponent implementation ---
    // These are required by the interface. They tell GH we manage our own params.

    public bool CanInsertParameter(GH_ParameterSide side, int index) => false;
    public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;
    public IGH_Param CreateParameter(GH_ParameterSide side, int index) => new Param_GenericObject();
    public bool DestroyParameter(GH_ParameterSide side, int index) => true;
    public void VariableParameterMaintenance() { }

    public override Guid ComponentGuid => new("8C6EAEFD-9895-4199-8603-A14F6890E522");
}