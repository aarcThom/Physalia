// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using GhJSON.Core;
using GhJSON.Core.PatchModels;
using GhJSON.Core.SchemaModels;
using Physalia.Core.Grounding.Components;
using Physalia.Core.Validation;

namespace Physalia.GH.Generation;

/// <summary>
/// Pre-placement lint of model-authored graphs. Two statically knowable defects are caught before
/// anything touches the canvas: a required input (no built-in default, not optional — the same
/// introspection that puts the <c>*</c> marker in the grounding) left with neither a wire nor
/// internalized data, which costs a full solve-and-feedback round of "failed to collect data"
/// warnings and null cascades; and multiple wires collecting into an item-access input, which GH
/// silently accepts as a list that multiplies every downstream item — almost always the model
/// meant one combined value, and the resulting duplicate geometry defies post-hoc diagnosis.
/// </summary>
internal static partial class GhJsonBridge
{
    /// <summary>
    /// Parses a GhJSON string and lints its required inputs, without touching the canvas. This is
    /// the standalone entry the Required Input Check guardrail calls before the payload reaches
    /// the Component Transmitter — the same defect the placement and patch paths once refused
    /// inline, now a single visible pipeline node. Handles both a full GhJSON graph (lint every
    /// component) and a ghpatch (lint only its added components, against the connections the patch
    /// adds). Malformed JSON is the transmitter's to report, so it passes through with no
    /// violations.
    /// </summary>
    /// <param name="json">The payload as a string — a full GhJSON graph or a ghpatch.</param>
    /// <returns>One violation line per unmet required input; empty when clean or not applicable.</returns>
    internal static IReadOnlyList<string> LintRequiredInputsJson(string json)
    {
        if (GhPatchDetector.IsGhPatch(json))
        {
            return LintPatchAdds(json);
        }

        GhJsonDocument doc;
        try
        {
            doc = GhJson.FromJson(json);
        }
        catch
        {
            // Malformed JSON: not this check's concern — the Component Transmitter reports it.
            return Array.Empty<string>();
        }

        if (doc.Components is null || doc.Components.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Resolve name→guid so port.Required introspection works, exactly as the placement path
        // did before linting. StampComponentGuids is idempotent and skips cluster nodes. The lint
        // reads the FULL connection list off the pristine document — the same view the placement
        // gate snapshotted before cluster/reference extraction — so cluster-fed inputs still read
        // as wired.
        StampComponentGuids(doc);
        return LintRequiredInputs(
            doc.Components,
            doc.Connections ?? Enumerable.Empty<GhJsonConnection>(),
            endpointIdsMustResolve: true);
    }

    /// <summary>
    /// Lints the ADDED components of a ghpatch: an added component whose required input has
    /// neither a wire (from any endpoint the patch adds) nor an internalized value is the same
    /// statically knowable defect the full-graph path catches. Only adds are checked — modified
    /// components already exist on the canvas with their wiring intact. Id remapping and pivot
    /// assignment are skipped (they matter only for the live apply, not for wired/internalized
    /// detection, which is invariant under a consistent id rename), so this reads the patch as
    /// authored.
    /// </summary>
    /// <param name="json">The ghpatch document as a string.</param>
    /// <returns>One violation line per unmet required input on an added component; empty when clean.</returns>
    private static IReadOnlyList<string> LintPatchAdds(string json)
    {
        GhPatchDocument patch;
        try
        {
            patch = GhJson.PatchFromJson(json);
        }
        catch
        {
            // Malformed ghpatch: not this check's concern — the Component Transmitter reports it.
            return Array.Empty<string>();
        }

        List<GhJsonComponent> adds = patch.Patch?.Components?.Add ?? new List<GhJsonComponent>();
        if (adds.Count == 0)
        {
            return Array.Empty<string>();
        }

        StampComponentGuids(new GhJsonDocument("1.0", null, adds, null, null));
        return LintRequiredInputs(
            adds,
            patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>(),
            endpointIdsMustResolve: false);
    }

    /// <summary>
    /// Checks the authored graph for statically knowable wiring defects: required inputs with
    /// neither a wire nor an internalized value; multiple wires collecting into an item-access
    /// input (they build a list and multiply every downstream item); connection endpoints that
    /// reference a port the component does not have (placement would drop the wire); and
    /// data-only components whose outputs nothing consumes (abandoned intent). Components whose
    /// type cannot be introspected are skipped (placement reports unknown components itself);
    /// the variable-parameter sentinel port is never required.
    /// </summary>
    /// <param name="components">The authored components (component-type guids already stamped).</param>
    /// <param name="connections">Every authored connection that could feed those components.</param>
    /// <param name="endpointIdsMustResolve">
    /// True on the full-document path, where every endpoint id must name an authored component;
    /// false on the patch path, where an id may resolve to a component already on the canvas.
    /// </param>
    /// <returns>One violation line per defect; empty when the graph is clean.</returns>
    private static List<string> LintRequiredInputs(
        IEnumerable<GhJsonComponent> components,
        IEnumerable<GhJsonConnection> connections,
        bool endpointIdsMustResolve)
    {
        List<GhJsonComponent> componentList = components.ToList();
        List<GhJsonConnection> connectionList = connections.ToList();
        connections = connectionList;
        components = componentList;
        // Signature map for every introspectable authored component, plus the full authored id
        // set (introspection failures included) so endpoint-id resolution is judged separately
        // from port-level checks.
        var sigById = new Dictionary<int, (GhJsonComponent Component, IReadOnlyList<ComponentPort> Inputs, IReadOnlyList<ComponentPort> Outputs)>();
        var authoredIds = new HashSet<int>();
        foreach (GhJsonComponent component in components)
        {
            if (component.Id is not int id)
            {
                continue;
            }

            authoredIds.Add(id);
            if (component.ComponentGuid is Guid typeGuid
                && ComponentSignatureProvider.TryGetSignature(typeGuid, out IReadOnlyList<ComponentPort> ins, out IReadOnlyList<ComponentPort> outs))
            {
                sigById[id] = (component, ins, outs);
            }
        }

        // Wire COUNTS per target component id, addressable by paramIndex and by name. Each
        // connection is counted exactly once — paramIndex preferred — so an endpoint authored
        // with both fields never double-counts. Source ids are collected for the orphan check.
        var wiredIndices = new Dictionary<int, Dictionary<int, int>>();
        var wiredNames = new Dictionary<int, Dictionary<string, int>>();
        var consumedIds = new HashSet<int>();
        foreach (GhJsonConnection conn in connections)
        {
            if (conn.From is { } source)
            {
                consumedIds.Add(source.Id);
            }

            if (conn.To is not { } to)
            {
                continue;
            }

            if (to.ParamIndex is int idx)
            {
                Dictionary<int, int> byIdx = wiredIndices.TryGetValue(to.Id, out Dictionary<int, int>? existing)
                    ? existing
                    : wiredIndices[to.Id] = new Dictionary<int, int>();
                byIdx[idx] = byIdx.TryGetValue(idx, out int n) ? n + 1 : 1;
            }
            else if (!string.IsNullOrWhiteSpace(to.ParamName))
            {
                Dictionary<string, int> byName = wiredNames.TryGetValue(to.Id, out Dictionary<string, int>? existing)
                    ? existing
                    : wiredNames[to.Id] = new Dictionary<string, int>(StringComparer.Ordinal);
                byName[to.ParamName!] = byName.TryGetValue(to.ParamName!, out int n) ? n + 1 : 1;
            }
        }

        var violations = new List<string>();

        // Endpoint validity: a wire referencing a port the component does not have is dropped at
        // placement with a conflict the model never sees pre-emptively — e.g. authoring "from
        // output paramIndex 1" on a single-output component. Checked against the same signatures
        // the required-input pass trusts; variable-parameter components (trailing "…" sentinel)
        // skip bounds checks because their live port count can exceed the default signature.
        foreach (GhJsonConnection conn in connections)
        {
            if (conn.From is { } from)
            {
                LintEndpoint(from, output: true, sigById, authoredIds, endpointIdsMustResolve, violations);
            }

            if (conn.To is { } target)
            {
                LintEndpoint(target, output: false, sigById, authoredIds, endpointIdsMustResolve, violations);
            }
        }

        foreach (GhJsonComponent component in components)
        {
            if (component.Id is not int id || !sigById.TryGetValue(id, out var sig))
            {
                continue;
            }

            IReadOnlyList<ComponentPort> inputs = sig.Inputs;
            for (int i = 0; i < inputs.Count; i++)
            {
                ComponentPort port = inputs[i];
                int wireCount = (wiredIndices.TryGetValue(id, out Dictionary<int, int>? byIdx) && byIdx.TryGetValue(i, out int nIdx) ? nIdx : 0)
                    + (wiredNames.TryGetValue(id, out Dictionary<string, int>? byName) && byName.TryGetValue(port.Name, out int nName) ? nName : 0);

                if (wireCount > 1 && port.Access == PortAccess.Item)
                {
                    violations.Add(
                        $"'{component.Name}' (id {id}) input '{port.Name}' (paramIndex {i}) receives {wireCount} wires but consumes ONE item — the wires collect into a {wireCount}-item list and every downstream item multiplies. Wire a single source, or combine the values upstream first (e.g. an Addition component).");
                }

                if (!port.Required)
                {
                    continue;
                }

                bool internalized = (component.InputSettings ?? Enumerable.Empty<GhJsonParameterSettings>())
                    .Any(s => s.InternalizedData is not null && s.ParameterName == port.Name);

                if (wireCount == 0 && !internalized)
                {
                    violations.Add(
                        $"'{component.Name}' (id {id}) input '{port.Name}' (paramIndex {i}) is required but has no wire and no internalized value — wire it or internalize a value.");
                }
            }

            // Orphan check: a component that CONSUMES data (has inputs — floating params like
            // sliders and Panels are exempt), produces ONLY data-typed outputs (geometry
            // terminals like a Domain Box ARE the result), and feeds nothing is almost always
            // abandoned intent — half of an idea the model never wired in.
            if (inputs.Count > 0
                && sig.Outputs.Count > 0
                && !consumedIds.Contains(id)
                && sig.Outputs.All(o => IsDataOnlyHint(o.TypeHint)))
            {
                string kinds = string.Join(", ", sig.Outputs.Select(o => o.TypeHint).Distinct());
                violations.Add(
                    $"'{component.Name}' (id {id}) produces only data ({kinds}) and nothing consumes its outputs — wire its result somewhere or remove the component; a dangling data component is almost always abandoned intent.");
            }
        }

        return violations.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Validates one connection endpoint against its component's authored signature: the id must
    /// resolve (full-document path only), and an authored paramIndex or paramName must name a
    /// port that exists on the referenced side.
    /// </summary>
    /// <param name="endpoint">The authored endpoint.</param>
    /// <param name="output">True when the endpoint is a FROM (output side); false for TO (input side).</param>
    /// <param name="sigById">Signatures of the introspectable authored components.</param>
    /// <param name="authoredIds">Every authored component id, introspectable or not.</param>
    /// <param name="idsMustResolve">Whether an unresolved id is a defect (full-document path).</param>
    /// <param name="violations">The violation sink.</param>
    private static void LintEndpoint(
        GhJsonConnectionEndpoint endpoint,
        bool output,
        Dictionary<int, (GhJsonComponent Component, IReadOnlyList<ComponentPort> Inputs, IReadOnlyList<ComponentPort> Outputs)> sigById,
        HashSet<int> authoredIds,
        bool idsMustResolve,
        List<string> violations)
    {
        if (!authoredIds.Contains(endpoint.Id))
        {
            if (idsMustResolve)
            {
                violations.Add(
                    $"a connection references component id {endpoint.Id}, which does not exist in the document — fix the endpoint id or remove the connection.");
            }

            return;
        }

        if (!sigById.TryGetValue(endpoint.Id, out var sig) || HasVariableParams(sig.Inputs))
        {
            return;
        }

        IReadOnlyList<ComponentPort> ports = output ? sig.Outputs : sig.Inputs;
        string side = output ? "output" : "input";

        bool badIndex = endpoint.ParamIndex is int idx && (idx < 0 || idx >= ports.Count);
        bool badName = endpoint.ParamIndex is null
            && !string.IsNullOrWhiteSpace(endpoint.ParamName)
            && !ports.Any(p => string.Equals(p.Name, endpoint.ParamName, StringComparison.Ordinal));

        if (badIndex || badName)
        {
            string authored = endpoint.ParamIndex is int i
                ? $"{side} paramIndex {i}"
                : $"{side} '{endpoint.ParamName}'";
            string available = string.Join(", ", ports.Select((p, n) => $"'{p.Name}' (paramIndex {n})"));
            violations.Add(
                $"a connection references {authored} on '{sig.Component.Name}' (id {endpoint.Id}), but its {side}s are: {available} — fix the {(output ? "from" : "to")} endpoint.");
        }
    }

    // The trailing "…" sentinel marks a variable-parameter component (Merge, Entwine, zui) whose
    // live port count can legitimately exceed the default signature.
    private static bool HasVariableParams(IReadOnlyList<ComponentPort> inputs) =>
        inputs.Any(p => p.Name == "…");

    // Output type hints that mean "this component's result IS the placed geometry" — legitimate
    // terminals the orphan check must never flag. Unknown/blank/Generic hints fail OPEN (treated
    // as possibly-geometry), mirroring the lint's skip-what-cannot-be-introspected policy.
    private static readonly HashSet<string> GeometryTypeHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "Point", "Line", "Curve", "Circle", "Arc", "Rectangle", "Polyline",
        "Surface", "Brep", "Mesh", "Box", "Geometry", "Extrusion", "SubD", "Group",
    };

    private static bool IsDataOnlyHint(string typeHint) =>
        !string.IsNullOrWhiteSpace(typeHint)
        && !string.Equals(typeHint, "Generic", StringComparison.OrdinalIgnoreCase)
        && !GeometryTypeHints.Contains(typeHint);
}
