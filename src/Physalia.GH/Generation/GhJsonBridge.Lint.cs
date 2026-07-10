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
            doc.Connections ?? Enumerable.Empty<GhJsonConnection>());
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
            patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>());
    }

    /// <summary>
    /// Checks every component's required inputs for a wire or an internalized value, and every
    /// item-access input for multiple wires (they collect into a list and multiply every
    /// downstream item — almost always the model intended a single combined value, e.g. wiring
    /// both an origin and an offset into one X coordinate instead of adding them first).
    /// Components whose type cannot be introspected are skipped (placement reports unknown
    /// components itself); the variable-parameter sentinel port is never required.
    /// </summary>
    /// <param name="components">The authored components (component-type guids already stamped).</param>
    /// <param name="connections">Every authored connection that could feed those components.</param>
    /// <returns>One violation line per defect; empty when the graph is clean.</returns>
    private static List<string> LintRequiredInputs(
        IEnumerable<GhJsonComponent> components,
        IEnumerable<GhJsonConnection> connections)
    {
        // Wire COUNTS per target component id, addressable by paramIndex and by name. Each
        // connection is counted exactly once — paramIndex preferred — so an endpoint authored
        // with both fields never double-counts.
        var wiredIndices = new Dictionary<int, Dictionary<int, int>>();
        var wiredNames = new Dictionary<int, Dictionary<string, int>>();
        foreach (GhJsonConnection conn in connections)
        {
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
        foreach (GhJsonComponent component in components)
        {
            if (component.Id is not int id
                || component.ComponentGuid is not Guid typeGuid
                || !ComponentSignatureProvider.TryGetSignature(typeGuid, out IReadOnlyList<ComponentPort> inputs, out _))
            {
                continue;
            }

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
        }

        return violations;
    }
}
