using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.Core.Parsing;

namespace Physalia.GH.Helpers;

public static class ParameterBuilder
{
    /// <summary>
    /// Removes all dynamic input and output parameters from the component,
    /// then rebuilds them based on the input and output definitions in the
    /// <see cref="ScriptResponse"/> returned by the LLM. Fixed parameters
    /// (Prompt, Send, Keys Path, Run, Script) are preserved.
    /// </summary>
    /// <param name="component">The Grasshopper component whose parameters will be rebuilt.</param>
    /// <param name="response">The parsed LLM response containing input and output definitions.</param>
    /// <param name="fixedInputCount">The number of permanent input parameters to preserve.</param>
    /// <param name="fixedOutputCount">The number of permanent output parameters to preserve.</param>
    public static void Rebuild(
        GH_Component component,
        ScriptResponse response,
        int fixedInputCount,
        int fixedOutputCount)
    {
        // Remove old dynamic inputs
        while (component.Params.Input.Count > fixedInputCount)
        {
            component.Params.UnregisterInputParameter(
                component.Params.Input[component.Params.Input.Count - 1]);
        }

        // Remove old dynamic outputs
        while (component.Params.Output.Count > fixedOutputCount)
        {
            component.Params.UnregisterOutputParameter(
                component.Params.Output[component.Params.Output.Count - 1]);
        }

        // Add new inputs
        foreach (var input in response.Inputs)
        {
            var param = CreateInputParam(input);
            component.Params.RegisterInputParam(param);
        }

        // Add new outputs
        foreach (var output in response.Outputs)
        {
            var param = CreateOutputParam(output);
            component.Params.RegisterOutputParam(param);
        }

        component.Params.OnParametersChanged();
    }

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
        param.Optional = true;

        param.Access = def.Access?.ToLowerInvariant() switch
        {
            "list" => GH_ParamAccess.list,
            "tree" => GH_ParamAccess.tree,
            _ => GH_ParamAccess.item
        };

        return param;
    }

    private static IGH_Param CreateOutputParam(ParamDefinition def)
    {
        return new Param_GenericObject
        {
            Name = def.PrettyName ?? def.Name,
            NickName = def.Name,
            Description = def.Tooltip ?? "",
            Access = GH_ParamAccess.item
        };
    }
}