// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Runtime.Code;
using RhinoCodePlatform.GH;

namespace Physalia.GH.Generation;

/// <summary>
/// Facade over the McNeel <c>RhinoCodePlatform.GH</c> API.
/// All access to the GH Python Script component goes through here so that
/// API changes between Rhino versions require edits in one place only.
/// </summary>
public static class GhPythonBridge
{
    /// <summary>
    /// Maps Physalia type-hint names (see the SystemPrompt type-hint vocabulary) to the
    /// CLR types used to build a typed <c>ParamType</c>. Keys are case-insensitive.
    /// </summary>
    private static readonly Dictionary<string, Type> TypeHintMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Number"]    = typeof(double),
        ["Integer"]   = typeof(int),
        ["Boolean"]   = typeof(bool),
        ["Text"]      = typeof(string),
        ["Point"]     = typeof(Rhino.Geometry.Point3d),
        ["Vector"]    = typeof(Rhino.Geometry.Vector3d),
        ["Plane"]     = typeof(Rhino.Geometry.Plane),
        ["Line"]      = typeof(Rhino.Geometry.Line),
        ["Circle"]    = typeof(Rhino.Geometry.Circle),
        ["Arc"]       = typeof(Rhino.Geometry.Arc),
        ["Curve"]     = typeof(Rhino.Geometry.Curve),
        ["Surface"]   = typeof(Rhino.Geometry.Surface),
        ["Brep"]      = typeof(Rhino.Geometry.Brep),
        ["Mesh"]      = typeof(Rhino.Geometry.Mesh),
        ["Geometry"]  = typeof(Rhino.Geometry.GeometryBase),
        ["Box"]       = typeof(Rhino.Geometry.Box),
        ["Transform"] = typeof(Rhino.Geometry.Transform),
        ["Interval"]  = typeof(Rhino.Geometry.Interval),
        ["Colour"]    = typeof(System.Drawing.Color),
    };

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
    /// Replaces all input parameters on the component with typed parameters built from
    /// <paramref name="specs"/>. Each spec's type hint maps to a <c>ParamType</c>
    /// (falling back to <c>ParamType.Any</c> when unknown) and its access to a
    /// <c>ScriptParamAccess</c>.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="specs">Typed parameter specs for the new input parameters.</param>
    public static void SetInputs(IGH_DocumentObject obj, IEnumerable<GhParamSpec> specs)
        => UpdateParams(obj, "UpdateInputParameters", specs);

    /// <summary>
    /// Replaces all output parameters on the component with typed parameters built from
    /// <paramref name="specs"/>.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="specs">Typed parameter specs for the new output parameters.</param>
    public static void SetOutputs(IGH_DocumentObject obj, IEnumerable<GhParamSpec> specs)
        => UpdateParams(obj, "UpdateOutputParameters", specs);

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

    /// <summary>
    /// Returns true once the component has been computed in the current solution,
    /// meaning its runtime messages reflect the latest solve rather than a pending
    /// expired state.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>true if the component's solution phase is Computed.</returns>
    public static bool HasComputed(IGH_DocumentObject obj)
        => obj is IGH_ActiveObject active && active.Phase == GH_SolutionPhase.Computed;

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void UpdateParams(IGH_DocumentObject obj, string methodName, IEnumerable<string> names)
    {
        var sc = Cast(obj);
        var specs = names.Select(n =>
            new ScriptParamSpec(n, ParamType.Any, n, string.Empty, ScriptParamAccess.Item));

        InvokeUpdate(obj, methodName, sc.LanguageSpec, specs);
    }

    private static void UpdateParams(IGH_DocumentObject obj, string methodName, IEnumerable<GhParamSpec> paramSpecs)
    {
        var sc = Cast(obj);
        var specs = paramSpecs.Select(p =>
            new ScriptParamSpec(p.Name, MapParamType(p.TypeHint), p.Name, string.Empty, MapScriptAccess(p.Access)));

        InvokeUpdate(obj, methodName, sc.LanguageSpec, specs);
    }

    private static void InvokeUpdate(IGH_DocumentObject obj, string methodName, object languageSpec, IEnumerable<ScriptParamSpec> specs)
    {
        var method = FindMethod(obj.GetType(), methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{methodName} not found on component type.");

        method.Invoke(obj, new object[] { languageSpec, specs });
    }

    /// <summary>
    /// Maps a Physalia type-hint name to a McNeel <c>ParamType</c>. Unknown or empty
    /// hints (and any hint whose CLR type cannot be wrapped) fall back to <c>ParamType.Any</c>,
    /// preserving the untyped behaviour of the name-only overloads.
    /// </summary>
    /// <param name="typeHint">Physalia type-hint name, e.g. <c>Number</c> or <c>Curve</c>.</param>
    /// <returns>The mapped <c>ParamType</c>, or <c>ParamType.Any</c>.</returns>
    private static ParamType MapParamType(string typeHint)
    {
        if (string.IsNullOrWhiteSpace(typeHint) || !TypeHintMap.TryGetValue(typeHint, out Type? clrType))
            return ParamType.Any;

        try
        {
            return new ParamType(clrType);
        }
        catch
        {
            return ParamType.Any;
        }
    }

    private static ScriptParamAccess MapScriptAccess(GhScriptParamAccess access) => access switch
    {
        GhScriptParamAccess.List => ScriptParamAccess.List,
        GhScriptParamAccess.Tree => ScriptParamAccess.Tree,
        _ => ScriptParamAccess.Item,
    };

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
