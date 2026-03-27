// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.Core.Config;
using Physalia.Core.Parsing;

namespace Physalia.GH.Helpers;

/// <summary>
/// Static helper methods for creating and rebuilding Grasshopper parameters from LLM responses,
/// and for inspecting user-defined dynamic parameters on a ZOOID component.
/// </summary>
public static class ParamBuilder
{
    /// <summary>
    /// Clear all current parameters on a component and rebuild the new ones based on the LLM response.
    /// Connections on params whose NickName survives in the new response are preserved.
    /// </summary>
    /// <param name="component">GH component to rebuild parameters for.</param>
    /// <param name="response">JSON response returned by the LLM.</param>
    public static void Rebuild(GH_Component component, ScriptResponse response)
    {
        // Snapshot connections by NickName before clearing
        var inputSources = component.Params.Input
            .ToDictionary(p => p.NickName, p => p.Sources.ToList());
        var outputRecipients = component.Params.Output
            .ToDictionary(p => p.NickName, p => p.Recipients.ToList());

        component.Params.Clear();

        // Add new inputs and restore any matching connections
        foreach (var input in response.Inputs)
        {
            var param = CreateInputParam(input);
            component.Params.RegisterInputParam(param);

            if (inputSources.TryGetValue(input.Name, out var sources))
            {
                foreach (var source in sources)
                {
                    param.AddSource(source);
                }
            }
        }

        // Add new outputs and restore any matching connections
        foreach (var output in response.Outputs)
        {
            var param = CreateOutputParam(output);
            component.Params.RegisterOutputParam(param);

            if (outputRecipients.TryGetValue(output.Name, out var recipients))
            {
                foreach (var recipient in recipients)
                {
                    recipient.AddSource(param);
                }
            }
        }

        component.Params.OnParametersChanged();
    }

    /// <summary>
    /// Returns all user-defined parameters from a parameter list.
    /// </summary>
    /// <param name="paramsIn">The list of GH parameters to inspect.</param>
    /// <param name="isInput">True if the parameters are inputs; false for outputs.</param>
    /// <returns>A list of ParamInfo records for each  parameter found.</returns>
    public static List<ParamInfo> GetUserParams(List<IGH_Param> paramsIn, bool isInput)
    {
        var paramList = new List<ParamInfo>();

        foreach (IGH_Param param in paramsIn)
        {
            /*
             * NOTE: I THINK I ALWAYS WANT TO KEEP USER CREATED PARAMS.
            // keep the parameter if it is user created
            var lowerDescription = param.Description.ToLower();
            if (!lowerDescription.Contains(PhyConstants.DefaultParamDescription))
            {
                continue;
            }
            */

            string access = GetParamAccess(param);
            string type = isInput ? GetInputType(param) : string.Empty;
            var connections = isInput ? param.Sources.ToList() : param.Recipients.ToList();

            paramList.Add(new ParamInfo(param.NickName, type, access, connections));
        }

        return paramList;
    }

    /// <summary>
    /// Returns the access mode of a Grasshopper parameter as a lowercase string.
    /// </summary>
    /// <param name="param">The parameter to inspect.</param>
    /// <returns>"list", "tree", or "item".</returns>
    public static string GetParamAccess(IGH_Param param)
    {
        return param.Access switch
        {
            GH_ParamAccess.list => "list",
            GH_ParamAccess.tree => "tree",
            _ => "item"
        };
    }

    /// <summary>
    /// Builds an LLM prompt section instructing the model to preserve user-defined parameters.
    /// </summary>
    /// <param name="inputs">User-defined input parameters to include in the prompt.</param>
    /// <param name="outputs">User-defined output parameters to include in the prompt.</param>
    /// <returns>
    /// A formatted string for appending to the LLM system prompt,
    /// or an empty string if both lists are empty.
    /// </returns>
    public static string UserParamsPrompt(List<ParamInfo> inputs, List<ParamInfo> outputs)
    {
        if (inputs.Count == 0 && outputs.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        if (inputs.Count > 0)
        {
            sb.AppendLine("YOU MUST INCLUDE THE FOLLOWING INPUT PARAMETERS IN YOUR RESPONSE (do not rename or reorder them):");
            sb.AppendLine();
            foreach (var p in inputs)
            {
                sb.AppendLine($"  name: {p.name}");
                sb.AppendLine($"  prettyName: <derive a human-readable label from \"{p.name}\">");
                sb.AppendLine($"  tooltip: <short description derived from your implementation.");
                sb.AppendLine($"  typeHint: {p.type}");
                sb.AppendLine($"  access: {p.paramAccess}");
                sb.AppendLine($"  optional: false");
                sb.AppendLine();
            }
        }

        if (outputs.Count > 0)
        {
            sb.AppendLine("YOU MUST INCLUDE THE FOLLOWING OUTPUT PARAMETERS IN YOUR RESPONSE (do not rename or reorder them):");
            sb.AppendLine();
            foreach (var p in outputs)
            {
                sb.AppendLine($"  name: {p.name}");
                sb.AppendLine($"  prettyName: <derive a human-readable label from \"{p.name}\">");
                sb.AppendLine($"  tooltip: <short description derived from your implementation>");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Lightweight snapshot of a user-defined parameter, used when constructing the LLM prompt.
    /// </summary>
    /// <param name="name">The NickName (variable name) of the parameter.</param>
    /// <param name="type">The type hint string, e.g. "Number" or "Point".</param>
    /// <param name="paramAccess">The access mode: "item", "list", or "tree".</param>
    /// <param name="connectedSources">The GH params wired to this parameter.</param>
    public record ParamInfo(string name, string type, string paramAccess, List<IGH_Param> connectedSources);

    // Gets the type hint from connected sources.
    // Falls back to an inference instruction if the param has no sources or only generic sources.
    private static string GetInputType(IGH_Param param)
    {
        foreach (IGH_Param source in param.Sources)
        {
            string typeName = source.TypeName;
            if (typeName != "Generic Data")
            {
                return typeName;
            }
        }

        return "<Infer from your implementation and name>";
    }

    private static IGH_Param CreateInputParam(ParamDefinition def)
    {
        IGH_Param param = def.TypeHint?.ToLowerInvariant() switch
        {
            "number" => new Param_Number(),
            "integer" => new Param_Integer(),
            "boolean" => new Param_Boolean(),
            "text" => new Param_String(),
            "point" => new Param_Point(),
            "vector" => new Param_Vector(),
            "plane" => new Param_Plane(),
            "line" => new Param_Line(),
            "circle" => new Param_Circle(),
            "arc" => new Param_Arc(),
            "curve" => new Param_Curve(),
            "surface" => new Param_Surface(),
            "brep" => new Param_Brep(),
            "mesh" => new Param_Mesh(),
            "geometry" => new Param_Geometry(),
            "box" => new Param_Box(),
            "transform" => new Param_Transform(),
            "interval" => new Param_Interval(),
            "colour" => new Param_Colour(),
            _ => new Param_GenericObject()
        };

        param.Name = def.PrettyName ?? def.Name;
        param.NickName = def.Name;
        param.Description = def.Tooltip ?? string.Empty;
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
            Description = def.Tooltip ?? string.Empty,
            Access = GH_ParamAccess.item,
        };
    }
}
