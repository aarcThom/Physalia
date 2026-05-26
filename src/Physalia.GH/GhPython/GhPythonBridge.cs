// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using RhinoCodePlatform.GH;

namespace Physalia.GH.GhPython;

/// <summary>
/// Facade over the McNeel <c>RhinoCodePlatform.GH</c> API.
/// All access to the GH Python Script component goes through here so that
/// API changes between Rhino versions require edits in one place only.
/// </summary>
public static class GhPythonBridge
{
    /// <summary>
    /// Returns true if <paramref name="obj"/> is a GH Python Script component
    /// that this bridge can drive.
    /// </summary>
    /// <param name="obj">Any document object.</param>
    /// <returns>true if the object implements <c>IScriptComponent</c>.</returns>
    public static bool IsScriptComponent(IGH_DocumentObject obj)
        => obj is IScriptComponent;

    /// <summary>
    /// Sets the Python source code on the target component.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="code">Python source code string.</param>
    public static void SetScript(IGH_DocumentObject obj, string code)
        => Cast(obj).Text = code;

    /// <summary>
    /// Returns the Python source code currently stored on the component.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>The script source string.</returns>
    public static string GetScript(IGH_DocumentObject obj)
        => Cast(obj).Text;

    /// <summary>
    /// Returns the input parameters currently defined on the component.
    /// Useful when the user has pre-configured inputs via the GH UI.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>Read-only list of input parameter descriptors.</returns>
    public static IReadOnlyList<GhScriptParam> GetInputs(IGH_DocumentObject obj)
        => ReadParamSet(Cast(obj).Inputs);

    /// <summary>
    /// Returns the output parameters currently defined on the component.
    /// Useful when the user has pre-configured outputs via the GH UI.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>Read-only list of output parameter descriptors.</returns>
    public static IReadOnlyList<GhScriptParam> GetOutputs(IGH_DocumentObject obj)
        => ReadParamSet(Cast(obj).Outputs);

    /// <summary>
    /// Returns all runtime error messages produced by the last solve.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>List of error message strings.</returns>
    public static IReadOnlyList<string> GetErrors(IGH_DocumentObject obj)
        => GetMessages(obj, GH_RuntimeMessageLevel.Error);

    /// <summary>
    /// Returns all runtime warning messages produced by the last solve.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>List of warning message strings.</returns>
    public static IReadOnlyList<string> GetWarnings(IGH_DocumentObject obj)
        => GetMessages(obj, GH_RuntimeMessageLevel.Warning);

    /// <summary>
    /// Expires the component, triggering a re-solve on the next GH solution pass.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    public static void Expire(IGH_DocumentObject obj)
    {
        if (obj is IGH_ActiveObject active)
            active.ExpireSolution(true);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static IScriptComponent Cast(IGH_DocumentObject obj)
    {
        if (obj is IScriptComponent sc) return sc;
        throw new InvalidOperationException(
            $"Object '{obj?.NickName}' is not a GH Python Script component.");
    }

    private static IReadOnlyList<GhScriptParam> ReadParamSet(IEnumerable<IScriptParameter> paramSet)
    {
        var result = new List<GhScriptParam>();
        foreach (var p in paramSet)
        {
            result.Add(new GhScriptParam(
                Name: p.VariableName,
                PrettyName: p.PrettyName,
                Description: p.Description,
                Access: MapAccess(p.Access)));
        }

        return result;
    }

    private static GhScriptParamAccess MapAccess(ScriptParamAccess access) => access switch
    {
        ScriptParamAccess.List => GhScriptParamAccess.List,
        ScriptParamAccess.Tree => GhScriptParamAccess.Tree,
        _ => GhScriptParamAccess.Item,
    };

    private static IReadOnlyList<string> GetMessages(IGH_DocumentObject obj, GH_RuntimeMessageLevel level)
    {
        if (obj is IGH_ActiveObject active)
            return (IReadOnlyList<string>)active.RuntimeMessages(level);

        return Array.Empty<string>();
    }
}
