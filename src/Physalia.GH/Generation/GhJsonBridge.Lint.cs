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
/// Pre-placement lint of model-authored graphs. A required input (no built-in default, not
/// optional — the same introspection that puts the <c>*</c> marker in the grounding) left with
/// neither a wire nor internalized data is a statically knowable defect: placing it anyway costs
/// a full solve-and-feedback round of "failed to collect data" warnings and null cascades. The
/// lint catches it before anything touches the canvas, so the model gets one crisp, actionable
/// list instead.
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
    /// Checks every component's required inputs for a wire or an internalized value. Components
    /// whose type cannot be introspected are skipped (placement reports unknown components
    /// itself); the variable-parameter sentinel port is never required.
    /// </summary>
    /// <param name="components">The authored components (component-type guids already stamped).</param>
    /// <param name="connections">Every authored connection that could feed those components.</param>
    /// <returns>One violation line per unmet required input; empty when the graph is clean.</returns>
    private static List<string> LintRequiredInputs(
        IEnumerable<GhJsonComponent> components,
        IEnumerable<GhJsonConnection> connections)
    {
        // Wired inputs per target component id, addressable by paramIndex and by name.
        var wiredIndices = new Dictionary<int, HashSet<int>>();
        var wiredNames = new Dictionary<int, HashSet<string>>();
        foreach (GhJsonConnection conn in connections)
        {
            if (conn.To is not { } to)
            {
                continue;
            }

            if (to.ParamIndex is int idx)
            {
                (wiredIndices.TryGetValue(to.Id, out HashSet<int>? byIdx)
                    ? byIdx
                    : wiredIndices[to.Id] = new HashSet<int>()).Add(idx);
            }

            if (!string.IsNullOrWhiteSpace(to.ParamName))
            {
                (wiredNames.TryGetValue(to.Id, out HashSet<string>? byName)
                    ? byName
                    : wiredNames[to.Id] = new HashSet<string>(StringComparer.Ordinal)).Add(to.ParamName!);
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
                if (!port.Required)
                {
                    continue;
                }

                bool wired = (wiredIndices.TryGetValue(id, out HashSet<int>? byIdx) && byIdx.Contains(i))
                    || (wiredNames.TryGetValue(id, out HashSet<string>? byName) && byName.Contains(port.Name));
                bool internalized = (component.InputSettings ?? Enumerable.Empty<GhJsonParameterSettings>())
                    .Any(s => s.InternalizedData is not null && s.ParameterName == port.Name);

                if (!wired && !internalized)
                {
                    violations.Add(
                        $"'{component.Name}' (id {id}) input '{port.Name}' (paramIndex {i}) is required but has no wire and no internalized value — wire it or internalize a value.");
                }
            }
        }

        return violations;
    }
}
