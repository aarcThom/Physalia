// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Physalia.Core.Grounding.Components;

namespace Physalia.GH.Generation;

/// <summary>
/// Reads the default input/output signature (port nicknames + type names) of installed Grasshopper
/// components. The full catalog never carries signatures — instantiating every registered component
/// up front is expensive and some plug-in constructors throw — so signatures are introspected
/// lazily, one component type at a time, and cached by component GUID for the lifetime of the
/// process. Consumers (the <c>search_components</c> tool, the grounding enrichment) fall back to
/// name-only rendering when introspection fails. Instantiation happens on the caller's thread —
/// always call from the main (solve) thread, like the other catalog providers.
/// </summary>
internal static class ComponentSignatureProvider
{
    // Keyed by component-type GUID. A null value caches a FAILED introspection so a throwing
    // plug-in constructor is attempted exactly once per session, never once per call.
    private static readonly ConcurrentDictionary<Guid, (IReadOnlyList<ComponentPort> Inputs, IReadOnlyList<ComponentPort> Outputs)?> Cache = new();

    // Keyed by concrete param type. True when reading TypeName will NOT throw. GH_Param<T>.get_TypeName
    // instantiates a T (Activator.CreateInstance<T>) to read its type label; when T is an interface or
    // abstract goo (e.g. GH_Param<IGH_Goo>) and the param type does not override TypeName, that throw
    // trips Grasshopper's first-chance breakpoint dialog even though our own try/catch swallows it.
    // Cached because the reflection walk is per-type-invariant.
    private static readonly ConcurrentDictionary<Type, bool> TypeNameSafe = new();

    /// <summary>
    /// Lazily instantiates the component type once and returns its default input/output signature.
    /// Floating parameter objects (e.g. a Point parameter) report no inputs and a single output.
    /// </summary>
    /// <param name="componentGuid">The component-type GUID to introspect.</param>
    /// <param name="inputs">The input ports, or an empty list when unavailable.</param>
    /// <param name="outputs">The output ports, or an empty list when unavailable.</param>
    /// <returns>
    /// True when the signature is available; false when the proxy is missing, construction throws,
    /// or the object exposes no readable parameters.
    /// </returns>
    internal static bool TryGetSignature(
        Guid componentGuid,
        out IReadOnlyList<ComponentPort> inputs,
        out IReadOnlyList<ComponentPort> outputs)
    {
        var signature = Cache.GetOrAdd(componentGuid, Introspect);
        if (signature is null)
        {
            inputs = Array.Empty<ComponentPort>();
            outputs = Array.Empty<ComponentPort>();
            return false;
        }

        (inputs, outputs) = signature.Value;
        return true;
    }

    /// <summary>
    /// Reads ports from live parameters: FULL Name preferred, nickname as fallback, <c>TypeName</c>
    /// as the type hint. Full names, not the short canvas nicknames, because the one place the
    /// model must author a parameter name exactly — <c>inputSettings.parameterName</c> for
    /// internalized data — matches by full Name; a model that only ever saw <c>C</c> writes
    /// <c>"parameterName": "C"</c> and the internalization silently misses <c>Closed</c>. Wires are
    /// unaffected (matched by paramIndex). Shared with the Runtime Health Check, which reads placed
    /// components directly and so reflects their actual (zui) state.
    /// </summary>
    /// <param name="params">The parameters to read.</param>
    /// <param name="inputSide">
    /// True when reading the INPUT list: inputs with no built-in default that are not marked
    /// optional get <see cref="ComponentPort.Required"/>, so signatures can warn the model that
    /// leaving them unwired yields nulls or no output. Outputs are never required.
    /// </param>
    /// <returns>One port per parameter, in order.</returns>
    internal static IReadOnlyList<ComponentPort> ReadPorts(IEnumerable<IGH_Param> @params, bool inputSide = false)
    {
        var ports = new List<ComponentPort>();
        foreach (IGH_Param param in @params)
        {
            string portName = !string.IsNullOrWhiteSpace(param.Name) ? param.Name : param.NickName ?? string.Empty;
            ports.Add(new ComponentPort(portName, SafeTypeName(param), inputSide && IsRequiredInput(param)));
        }

        return ports;
    }

    // True when an input carries no built-in default value and is not flagged optional by its
    // component. PersistentDataCount lives on GH_PersistentParam<T>, not IGH_Param, so it is read
    // by reflection; a param type without the property has no default mechanism at all.
    private static bool IsRequiredInput(IGH_Param param)
    {
        if (param.Optional)
        {
            return false;
        }

        try
        {
            object? count = param.GetType().GetProperty("PersistentDataCount")?.GetValue(param);
            return count is not int n || n == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns a catalog whose entries carry their introspected signatures
    /// (<c>entry with { Inputs, Outputs }</c>). Entries that fail introspection pass through
    /// unchanged — their null ports render name-only downstream. Call only on an already-filtered
    /// catalog: every not-yet-cached entry costs one component instantiation.
    /// </summary>
    /// <param name="catalog">The catalog to enrich.</param>
    /// <returns>The enriched catalog.</returns>
    internal static ComponentCatalog EnrichWithSignatures(ComponentCatalog catalog)
    {
        return new ComponentCatalog(catalog.Entries
            .Select(e => TryGetSignature(e.ComponentGuid, out var ins, out var outs)
                ? e with { Inputs = ins, Outputs = outs }
                : e)
            .ToList());
    }

    /// <summary>
    /// Returns a catalog where only the entries named in <paramref name="onlyNames"/> carry their
    /// introspected signatures; every other entry passes through unchanged and renders name-only
    /// downstream. This is the default (hybrid) grounding shape: the curated common set costs at
    /// most one instantiation each, while the long tail stays a flat name list.
    /// </summary>
    /// <param name="catalog">The catalog to enrich.</param>
    /// <param name="onlyNames">The component names to enrich (case-insensitive).</param>
    /// <returns>The partially enriched catalog.</returns>
    internal static ComponentCatalog EnrichWithSignatures(ComponentCatalog catalog, IReadOnlySet<string> onlyNames)
    {
        return new ComponentCatalog(catalog.Entries
            .Select(e => onlyNames.Contains(e.Name) && TryGetSignature(e.ComponentGuid, out var ins, out var outs)
                ? e with { Inputs = ins, Outputs = outs }
                : e)
            .ToList());
    }

    // Instantiates the component type and reads its default ports. Returns null on any failure —
    // missing proxy, throwing plug-in constructor, or an object that is neither a component nor a
    // floating parameter. The instance is never added to a document; it is dropped after reading.
    private static (IReadOnlyList<ComponentPort> Inputs, IReadOnlyList<ComponentPort> Outputs)? Introspect(Guid componentGuid)
    {
        try
        {
            IGH_ObjectProxy? proxy = Instances.ComponentServer.EmitObjectProxy(componentGuid);
            switch (proxy?.CreateInstance())
            {
                case IGH_Component component:
                    var inputs = ReadPorts(component.Params.Input, inputSide: true);

                    // A variable-parameter component (Merge, Entwine, zui) can grow beyond its
                    // default inputs — a trailing "…" port signals that without lying about the
                    // default instance.
                    if (component is IGH_VariableParameterComponent)
                    {
                        inputs = inputs.Append(new ComponentPort("…", string.Empty)).ToList();
                    }

                    return (inputs, ReadPorts(component.Params.Output));
                case IGH_Param param:
                    string name = !string.IsNullOrWhiteSpace(param.Name) ? param.Name : param.NickName ?? string.Empty;
                    return (
                        Array.Empty<ComponentPort>(),
                        new[] { new ComponentPort(name, SafeTypeName(param)) });
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a parameter's <c>TypeName</c> without tripping Grasshopper's first-chance breakpoint
    /// dialog. <c>GH_Param&lt;T&gt;.get_TypeName</c> instantiates a <c>T</c> to read its label; for a
    /// generic-object param (<c>T = IGH_Goo</c> and no <c>TypeName</c> override) that throws inside
    /// <c>InstantiateT</c>, and the throw surfaces the diagnostic dialog even though it is caught.
    /// Params that override <c>TypeName</c> never reach that path, so they are read directly.
    /// </summary>
    /// <param name="param">The parameter to read.</param>
    /// <returns>The type name, or an empty string when it cannot be read safely.</returns>
    internal static string SafeTypeName(IGH_Param param)
    {
        if (!TypeNameSafe.GetOrAdd(param.GetType(), IsTypeNameSafe))
        {
            return "Generic";
        }

        try
        {
            return param.TypeName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // True when reading TypeName on this param type will not throw. It throws only when the getter is
    // the un-overridden GH_Param<T>.get_TypeName AND T cannot be default-constructed (interface,
    // abstract, or no public parameterless ctor). Any override — the common case — is safe.
    private static bool IsTypeNameSafe(Type paramType)
    {
        MethodInfo? getter = paramType.GetProperty("TypeName")?.GetGetMethod();
        Type? declaring = getter?.DeclaringType;
        if (declaring is null || !declaring.IsGenericType || declaring.GetGenericTypeDefinition() != typeof(GH_Param<>))
        {
            // Overridden by a concrete param (or no getter at all) — reading it never calls InstantiateT.
            return true;
        }

        Type goo = declaring.GetGenericArguments()[0];
        if (goo.IsInterface || goo.IsAbstract)
        {
            return false;
        }

        return goo.IsValueType || goo.GetConstructor(Type.EmptyTypes) is not null;
    }
}
