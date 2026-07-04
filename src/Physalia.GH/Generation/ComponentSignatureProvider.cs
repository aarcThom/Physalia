// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    /// Reads ports from live parameters: nickname preferred (the short label shown on the canvas,
    /// e.g. <c>G</c>), full name as fallback, <c>TypeName</c> as the type hint. Shared with the
    /// Canvas Observation, which reads placed components directly and so reflects their actual (zui) state.
    /// </summary>
    /// <param name="params">The parameters to read.</param>
    /// <returns>One port per parameter, in order.</returns>
    internal static IReadOnlyList<ComponentPort> ReadPorts(IEnumerable<IGH_Param> @params)
    {
        var ports = new List<ComponentPort>();
        foreach (IGH_Param param in @params)
        {
            string portName = !string.IsNullOrWhiteSpace(param.NickName) ? param.NickName : param.Name ?? string.Empty;
            ports.Add(new ComponentPort(portName, param.TypeName ?? string.Empty));
        }

        return ports;
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
                    var inputs = ReadPorts(component.Params.Input);

                    // A variable-parameter component (Merge, Entwine, zui) can grow beyond its
                    // default inputs — a trailing "…" port signals that without lying about the
                    // default instance.
                    if (component is IGH_VariableParameterComponent)
                    {
                        inputs = inputs.Append(new ComponentPort("…", string.Empty)).ToList();
                    }

                    return (inputs, ReadPorts(component.Params.Output));
                case IGH_Param param:
                    string name = !string.IsNullOrWhiteSpace(param.NickName) ? param.NickName : param.Name ?? string.Empty;
                    return (
                        Array.Empty<ComponentPort>(),
                        new[] { new ComponentPort(name, param.TypeName ?? string.Empty) });
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }
}
