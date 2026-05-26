// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Runtime.Code;
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
    /// Replaces all input parameters on the component with parameters built from
    /// <paramref name="names"/>. Each parameter uses <c>ParamType.Any</c> and Item access.
    /// <c>UpdateInputParameters</c> is on <c>BaseScriptComponent</c> (not the interface),
    /// so it is invoked via reflection.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="names">Variable names for the new input parameters.</param>
    public static void SetInputs(IGH_DocumentObject obj, IEnumerable<string> names)
        => UpdateParams(obj, "UpdateInputParameters", names);

    /// <summary>
    /// Replaces all output parameters on the component with parameters built from
    /// <paramref name="names"/>. Each parameter uses <c>ParamType.Any</c> and Item access.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="names">Variable names for the new output parameters.</param>
    public static void SetOutputs(IGH_DocumentObject obj, IEnumerable<string> names)
        => UpdateParams(obj, "UpdateOutputParameters", names);

    /// <summary>
    /// Returns the current volatile data of every GH input parameter on the component
    /// as <c>"name: value1, value2, ..."</c> strings.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>One string per input parameter.</returns>
    public static IReadOnlyList<string> GetInputValues(IGH_DocumentObject obj)
        => ReadParamValues(obj, input: true);

    /// <summary>
    /// Returns the current volatile data of every GH output parameter on the component
    /// as <c>"name: value1, value2, ..."</c> strings.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>One string per output parameter.</returns>
    public static IReadOnlyList<string> GetOutputValues(IGH_DocumentObject obj)
        => ReadParamValues(obj, input: false);

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

    private static void UpdateParams(IGH_DocumentObject obj, string methodName, IEnumerable<string> names)
    {
        var sc = Cast(obj);
        var specs = names.Select(n =>
            new ScriptParamSpec(n, ParamType.Any, n, string.Empty, ScriptParamAccess.Item));

        var method = FindMethod(obj.GetType(), methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{methodName} not found on component type.");

        method.Invoke(obj, new object[] { sc.LanguageSpec, specs });
    }

    private static IReadOnlyList<string> ReadParamValues(IGH_DocumentObject obj, bool input)
    {
        if (obj is not IGH_Component component)
            return Array.Empty<string>();

        var paramList = input ? component.Params.Input : component.Params.Output;
        var result = new List<string>(paramList.Count);

        foreach (var param in paramList)
        {
            var values = param.VolatileData.AllData(true)
                .Select(goo => goo?.ToString() ?? "null")
                .ToList();
            var valStr = values.Count == 0 ? "(empty)" : string.Join(", ", values);
            result.Add($"{param.Name}: {valStr}");
        }

        return result;
    }

    private static MethodInfo? FindMethod(Type type, string name, BindingFlags flags)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var m = t.GetMethod(name, flags);
            if (m != null) return m;
        }

        return null;
    }

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
