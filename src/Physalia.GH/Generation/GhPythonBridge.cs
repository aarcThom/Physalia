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
    /// Returns the input parameters currently defined on the component as push-shaped specs:
    /// variable name, the Physalia type-hint name read back from the parameter's converter
    /// (empty when untyped or when the hint has no Physalia name), and access. This is the
    /// read-back counterpart of <see cref="SetInputs(IGH_DocumentObject, IEnumerable{GhParamSpec})"/>,
    /// used to capture a locked interface exactly as the model must re-declare it.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>Read-only list of input parameter specs.</returns>
    public static IReadOnlyList<GhParamSpec> GetInputSpecs(IGH_DocumentObject obj)
        => Cast(obj).Inputs
            .Select(p => new GhParamSpec(p.VariableName, ReadTypeHintName(p), MapAccess(p.Access)))
            .ToList();

    /// <summary>
    /// Returns the output parameters currently defined on the component as push-shaped specs.
    /// Outputs never carry a type hint (see <see cref="SetOutputs(IGH_DocumentObject, IEnumerable{GhParamSpec})"/>),
    /// so the hint is always empty; only name and access are meaningful.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>Read-only list of output parameter specs.</returns>
    public static IReadOnlyList<GhParamSpec> GetOutputSpecs(IGH_DocumentObject obj)
        => Cast(obj).Outputs
            .Select(p => new GhParamSpec(p.VariableName, string.Empty, MapAccess(p.Access)))
            .ToList();

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
        => UpdateParams(obj, "UpdateInputParameters", specs, typed: true);

    /// <summary>
    /// Replaces all output parameters on the component with parameters built from
    /// <paramref name="specs"/>. Outputs are deliberately left untyped (<c>ParamType.Any</c>),
    /// only their access is honoured. A concretely-typed output param (e.g. GeometryBase) that
    /// the script hands a Python list throws a fatal <c>ParamConvertException</c> whenever the
    /// access is Item — and on the first push of a freshly generated script the RhinoCode engine
    /// forces Item access via auto-declaration (no compiled instance yet), clobbering any
    /// promoted List access. An untyped output is a <c>Param_GenericObject</c>, so it wraps
    /// whatever Python returns (a clean list under List access, a single generic goo under Item)
    /// and can never fail conversion. The access promotion still applies, producing a clean list
    /// once the access sticks.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="specs">Parameter specs for the new output parameters; type hints are ignored.</param>
    public static void SetOutputs(IGH_DocumentObject obj, IEnumerable<GhParamSpec> specs)
        => UpdateParams(obj, "UpdateOutputParameters", specs, typed: false);

    /// <summary>
    /// Turns on output marshalling for the component (the engine's <c>MarshOutputs</c> flag, the
    /// inverse of the editor's "Avoid Marshalling Outputs" menu item), so Python output values are
    /// converted to GH-native data before assignment.
    /// <para>This is what actually flattens a list output. The engine assigns an output as a list
    /// (<c>SetDataList</c>) only when the captured value is a .NET enumerable; with marshalling off it
    /// stores the raw Python object, which GH wraps as a single opaque <c>GH_ObjectWrapper&lt;PyObject&gt;</c>
    /// (one item, never flattened) regardless of access or type hint. The flag defaults on for a
    /// hand-made component but is copied from the script when code is set, so a pushed script can land
    /// with it off. Forcing it on makes the engine marshal a Python list to a .NET list, which the GH
    /// layer then expands into individual items.</para>
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    public static void EnableOutputMarshalling(IGH_DocumentObject obj)
    {
        for (Type? t = obj.GetType(); t != null; t = t.BaseType)
        {
            PropertyInfo? prop = t.GetProperty("MarshOutputs",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.CanWrite == true)
            {
                prop.SetValue(obj, true);
                return;
            }
        }
    }

    /// <summary>
    /// Pins the named output parameters to the "No Type Hint" converter, undoing the type hint the
    /// engine installs on a freshly built Python output.
    /// <para>An LLM output handed a Python value (especially a list) must use No Type Hint: the
    /// default "ghdoc Object" converter wraps whatever Python returns as a single opaque
    /// <c>GH_ObjectWrapper&lt;PyObject&gt;</c> — even under List access — so a list never flattens and
    /// downstream conversion fails. <c>SetOutputs</c> pushes <c>ParamType.Any</c>, which resolves to
    /// the No-Type-Hint (Goo) converter, but the engine's <c>VariableParameterMaintenance</c> then
    /// swaps any Goo converter on a Python param for the user's configured Default Python Hint (often
    /// "ghdoc Object"). This re-pins No Type Hint <em>after</em> that swap, through the same path the
    /// editor's hint menu uses (the <c>IScriptParameter.Converter</c> setter, which re-stamps via
    /// <c>ParamsApply</c> and does not re-run the converter swap), so it sticks. Setting the converter
    /// to null is how the engine itself denotes No Type Hint — it coerces to the Goo converter.</para>
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="names">Variable names of the outputs to pin to No Type Hint.</param>
    public static void SetOutputsNoTypeHint(IGH_DocumentObject obj, IEnumerable<string> names)
    {
        if (obj is not IGH_Component component)
            return;

        var target = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (IGH_Param param in component.Params.Output)
        {
            if (param is IScriptParameter scriptParam && target.Contains(scriptParam.VariableName))
                SetConverterToNoTypeHint(param);
        }
    }

    /// <summary>
    /// Re-applies the declared access to existing output parameters in place, mirroring the
    /// editor's right-click "List/Tree Access" path: it flips each matching parameter's
    /// <c>Access</c> and re-applies the script signature via <c>IScriptObject.ParamsApply</c>,
    /// without removing or re-registering any parameter.
    /// <para>This exists because the GH Python Script component auto-declares its parameters
    /// item-access whenever it has no compiled instance, which is the case on the first push of
    /// every freshly generated script — silently overriding the access set by <see cref="SetOutputs"/>.
    /// Restructuring the parameter set (as <c>UpdateOutputParameters</c> does) re-invalidates the
    /// instance and re-triggers that clobber, but an in-place access change after the component has
    /// solved once leaves the instance intact, so the engine honours the explicit access. Call this
    /// only once the target has computed (an instance exists), then expire it to re-solve.</para>
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="accessByName">Map of output variable name to the access to apply.</param>
    public static void ApplyOutputAccess(IGH_DocumentObject obj, IReadOnlyDictionary<string, GhScriptParamAccess> accessByName)
        => ApplyParamAccess(obj, input: false, accessByName);

    /// <summary>
    /// In-place access re-apply for input parameters; see <see cref="ApplyOutputAccess"/>.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="accessByName">Map of input variable name to the access to apply.</param>
    public static void ApplyInputAccess(IGH_DocumentObject obj, IReadOnlyDictionary<string, GhScriptParamAccess> accessByName)
        => ApplyParamAccess(obj, input: true, accessByName);

    /// <summary>
    /// Clears the auto-declare flag on the named parameters of the compiled output signature, forcing
    /// the engine to honour their declared access and converter instead of re-deriving them from the
    /// code.
    /// <para>This is the final piece of the list-output fix. The engine sets <c>AutoDeclare=true</c> on
    /// every param whenever <c>Script.GetInstanceInfo().HasInstance</c> is false — which it remains
    /// through the corrective access re-stamp — and an auto-declared output marshals a Python list as a
    /// single wrapped object regardless of the (correctly set) List access. Clearing the flag directly
    /// on the live compiled params after the access re-stamp reproduces the end state of the editor's
    /// manual "List Access" (access=List, autoDeclare=false), so the list flattens. Call after
    /// <see cref="ApplyOutputAccess"/> and before expiring; the marshalling solve does not rebuild the
    /// signature, so the cleared flag survives to drive marshalling.</para>
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <param name="names">Variable names of the outputs whose auto-declare flag to clear.</param>
    public static void ClearOutputAutoDeclare(IGH_DocumentObject obj, IEnumerable<string> names)
    {
        var target = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (object param in GetCompiledOutputParams(obj))
        {
            string? name = GetMember(param, "Name")?.ToString();
            if (name is null || !target.Contains(name))
                continue;

            for (Type? t = param.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo? prop = t.GetProperty("AutoDeclare",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop?.CanWrite == true)
                {
                    prop.SetValue(param, false);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Walks the RhinoCode object graph (<c>Context → Script → GetCode() → Outputs</c>) by reflection
    /// and returns the live parameters of the compiled output signature. Returns an empty sequence if
    /// any link is missing.
    /// </summary>
    /// <param name="obj">The GH Python Script component.</param>
    /// <returns>The compiled output parameter objects.</returns>
    private static IEnumerable<object> GetCompiledOutputParams(IGH_DocumentObject obj)
    {
        object? context = GetMember(obj, "Context");
        object? script = context is null ? null : GetMember(context, "Script");
        object? code = script?.GetType()
            .GetMethod("GetCode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)
            ?.Invoke(script, null);

        if (code is null || GetMember(code, "Outputs") is not System.Collections.IEnumerable outputs)
            return Array.Empty<object>();

        var result = new List<object>();
        foreach (object? param in outputs)
        {
            if (param != null)
                result.Add(param);
        }

        return result;
    }

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

    private static void UpdateParams(IGH_DocumentObject obj, string methodName, IEnumerable<GhParamSpec> paramSpecs, bool typed)
    {
        var sc = Cast(obj);
        var specs = paramSpecs.Select(p =>
            new ScriptParamSpec(p.Name, typed ? MapParamType(p.TypeHint) : ParamType.Any, p.Name, string.Empty, MapScriptAccess(p.Access)));

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

    private static void ApplyParamAccess(IGH_DocumentObject obj, bool input, IReadOnlyDictionary<string, GhScriptParamAccess> accessByName)
    {
        if (obj is not IGH_Component component || accessByName is null || accessByName.Count == 0)
            return;

        var paramList = input ? component.Params.Input : component.Params.Output;
        bool applied = false;
        foreach (IGH_Param param in paramList)
        {
            if (param is IScriptParameter scriptParam
                && accessByName.TryGetValue(scriptParam.VariableName, out GhScriptParamAccess access))
            {
                param.Access = MapToGhParamAccess(access);
                applied = true;
            }
        }

        // ParamsApply re-stamps the script signature from the parameters. With an instance now
        // present (the target has solved once) GetContextOutputs runs with AutoDeclare=false, so the
        // engine honours the declared access instead of auto-declaring Item.
        //
        // This MUST fire even when no Access value changed. The first-push clobber does not corrupt
        // param.Access — SetOutputs already wrote the intended List access onto the param. The clobber
        // lives in the runtime ScriptParam's AutoDeclare flag (set true while there is no instance),
        // which silently overrides the declared access at solve time. So at this point param.Access is
        // already List and a value-change guard would never trigger; the corrective re-stamp is the
        // whole point of this pass, so it runs whenever a targeted param is present.
        //
        // IScriptObject is an explicitly-implemented interface in a transitive RhinoCode assembly not
        // referenced at compile time, so it is reached by interface name via reflection.
        if (applied)
            InvokeParamsApply(obj);
    }

    /// <summary>
    /// Sets a script parameter's type-hint converter to "No Type Hint" by assigning null through the
    /// explicitly-implemented <c>IScriptParameter.Converter</c> setter (reached by interface name via
    /// reflection, since its declaring assembly is not referenced at compile time). The setter coerces
    /// null to the Goo converter — the engine's representation of No Type Hint — and re-stamps the
    /// script signature without re-running the converter swap, so the hint sticks across the re-solve.
    /// </summary>
    /// <param name="param">The script output parameter to re-hint.</param>
    private static void SetConverterToNoTypeHint(IGH_Param param)
    {
        Type? scriptParamInterface = Array.Find(
            param.GetType().GetInterfaces(), i => i.Name == "IScriptParameter");

        scriptParamInterface?.GetProperty("Converter")?.SetValue(param, null);
    }

    /// <summary>
    /// Reads a public or non-public instance property or field by name off an object, searching the
    /// type hierarchy. Returns null if not found or on any access error. Used to walk the RhinoCode
    /// object graph (Context → Script → Outputs → param) reflectively, since those types are not
    /// referenced at compile time.
    /// </summary>
    /// <param name="instance">The object to read from; may be null.</param>
    /// <param name="memberName">The property or field name.</param>
    /// <returns>The member value, or null.</returns>
    private static object? GetMember(object? instance, string memberName)
    {
        if (instance is null)
            return null;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (Type? t = instance.GetType(); t != null; t = t.BaseType)
        {
            PropertyInfo? prop = t.GetProperty(memberName, flags);
            if (prop != null)
                return prop.GetValue(instance);

            FieldInfo? field = t.GetField(memberName, flags);
            if (field != null)
                return field.GetValue(instance);
        }

        return null;
    }

    private static void InvokeParamsApply(IGH_DocumentObject obj)
    {
        Type? scriptObjectInterface = Array.Find(
            obj.GetType().GetInterfaces(), i => i.Name == "IScriptObject");

        scriptObjectInterface?.GetMethod("ParamsApply", Type.EmptyTypes)?.Invoke(obj, null);
    }

    private static GH_ParamAccess MapToGhParamAccess(GhScriptParamAccess access) => access switch
    {
        GhScriptParamAccess.List => GH_ParamAccess.list,
        GhScriptParamAccess.Tree => GH_ParamAccess.tree,
        _ => GH_ParamAccess.item,
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

    /// <summary>
    /// Reads the Physalia type-hint name back off a script parameter's converter: the converter's
    /// target CLR type is reverse-mapped through <see cref="TypeHintMap"/>. A null converter (No
    /// Type Hint), an unmappable target type, or any converter access failure yields empty — the
    /// untyped representation, matching what <c>MapParamType</c> would build from an empty hint.
    /// </summary>
    /// <param name="param">The script parameter to read.</param>
    /// <returns>The Physalia type-hint name (e.g. <c>Number</c>), or empty when untyped.</returns>
    private static string ReadTypeHintName(IScriptParameter param)
    {
        try
        {
            Type? clrType = param.Converter?.TargetType?.Type;
            if (clrType is null)
                return string.Empty;

            foreach (KeyValuePair<string, Type> pair in TypeHintMap)
            {
                if (pair.Value == clrType)
                    return pair.Key;
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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
