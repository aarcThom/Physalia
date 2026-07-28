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
/// Pre-placement lint of model-authored graphs. Statically knowable defects are caught before
/// anything touches the canvas: a required input (no built-in default, not optional — the same
/// introspection that puts the <c>*</c> marker in the grounding) left with neither a wire nor
/// internalized data, which costs a full solve-and-feedback round of "failed to collect data"
/// warnings and null cascades; multiple wires collecting into an item-access input, which GH
/// silently accepts as a list that multiplies every downstream item; a component whose outputs
/// nothing consumes, which is half an idea the model never wired in; and an operator taking both
/// operands from one source, which combines a value with itself. The last two are the defects a
/// solve cannot report — the graph runs clean and produces geometry, it is just not the geometry
/// the model believes it built, so the model keeps confirming success against numbers that
/// contradict it.
/// </summary>
internal static partial class GhJsonBridge
{
    /// <summary>
    /// Parses a GhJSON string and lints it, without touching the canvas. This is the standalone
    /// entry the Required Input Check guardrail calls before the payload reaches the Component
    /// Transmitter — the same defect the placement and patch paths once refused inline, now a
    /// single visible pipeline node. Handles both a full GhJSON graph (lint every component) and a
    /// ghpatch (lint the graph the patch would produce). Malformed JSON is the transmitter's to
    /// report, so it passes through with no violations.
    /// </summary>
    /// <param name="json">The payload as a string — a full GhJSON graph or a ghpatch.</param>
    /// <returns>One violation line per defect; empty when clean or not applicable.</returns>
    internal static IReadOnlyList<string> LintRequiredInputsJson(string json)
    {
        if (GhPatchDetector.IsGhPatch(json))
        {
            return LintPatch(json);
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
        return Messages(LintGraph(
            doc.Components,
            doc.Connections ?? Enumerable.Empty<GhJsonConnection>(),
            endpointIdsMustResolve: true));
    }

    /// <summary>
    /// Lints the graph a ghpatch would PRODUCE — the canvas with the patch's adds, removes and
    /// wire surgery applied — rather than its added components in isolation. Wire surgery is where
    /// the defects this check exists for actually appear: a patch that only moves wires used to get
    /// no review at all, so it could strand a component (remove its last consumer and leave it on
    /// the canvas) or land a second wire from one source onto both operands of an Addition, and
    /// nothing said a word. Findings are scoped to what the patch touched, per kind, so the user's
    /// own dangling canvas objects — which the model neither authored nor can fix — are never
    /// reported back at it.
    /// </summary>
    /// <param name="json">The ghpatch document as a string.</param>
    /// <returns>One violation line per defect the patch would introduce; empty when clean.</returns>
    private static IReadOnlyList<string> LintPatch(string json)
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

        if (!HasOperations(patch))
        {
            return Array.Empty<string>();
        }

        List<GhJsonComponent> adds = patch.Patch?.Components?.Add ?? new List<GhJsonComponent>();
        if (adds.Count > 0)
        {
            StampComponentGuids(new GhJsonDocument("1.0", null, adds, null, null));
        }

        // Project against the frame the model authored in (same matching rule as the apply): a
        // checksum matching the group-scoped export means it saw only the master group's contents,
        // and linting the full canvas would name components it cannot see.
        GhJsonDocument? canvas = ResolveBaseSnapshot(null, patch.Patch?.Base?.Checksum?.Trim())?.Document;
        if (canvas?.Components is null || canvas.Components.Count == 0)
        {
            // Nothing to project onto (no document, or an empty canvas the model should not have
            // sent a patch against): fall back to linting the adds in isolation, which is what this
            // check did before the projected graph existed.
            return adds.Count == 0
                ? Array.Empty<string>()
                : Messages(LintGraph(
                    adds,
                    patch.Patch?.Connections?.Add ?? Enumerable.Empty<GhJsonConnection>(),
                    endpointIdsMustResolve: false));
        }

        // Mirror the apply's id remapping before projecting, so the lint reasons about the same
        // graph the apply will build: an add whose id collides with a canvas id is renumbered and
        // every patch endpoint naming it is rewritten to the add (the spec's rewriting rule).
        RemapCollidingAddIds(patch, adds, canvas);

        var index = BuildBaseIndex(canvas);
        List<GhJsonConnection> connectionAdds = patch.Patch?.Connections?.Add ?? new List<GhJsonConnection>();
        List<GhJsonConnection> connectionRemoves = patch.Patch?.Connections?.Remove ?? new List<GhJsonConnection>();

        var addIds = new HashSet<int>(adds.Where(a => a.Id is int).Select(a => a.Id!.Value));
        var removedIds = new HashSet<int>();
        foreach (GhPatchComponentMatch match in patch.Patch?.Components?.Remove ?? Enumerable.Empty<GhPatchComponentMatch>())
        {
            if (ResolveComponentMatch(match, canvas, index)?.Id is int removedId)
            {
                removedIds.Add(removedId);
            }
        }

        // Components whose VALUE the patch changes. They earn the orphan check even though a modify
        // cannot alter wiring: "you just changed a value source that drives nothing" is the single
        // most useful thing to say to a model that thinks it moved the geometry. Without it a dead
        // slider absorbs modify after modify while unrelated edits elsewhere make the report move.
        var modifiedIds = new HashSet<int>();
        foreach (GhPatchComponentModify modify in patch.Patch?.Components?.Modify ?? Enumerable.Empty<GhPatchComponentModify>())
        {
            if (ResolveComponentMatch(modify.Match, canvas, index)?.Id is int modifiedId)
            {
                modifiedIds.Add(modifiedId);
            }
        }

        List<GhJsonComponent> projected = canvas.Components
            .Where(c => c.Id is not int cid || !removedIds.Contains(cid))
            .Concat(adds)
            .ToList();

        // One normalization pass over every wire in play — canvas, adds and removes — so a removal
        // authored by paramName resolves to the index the canvas export addressed the same port by,
        // and the identity comparison below actually matches it.
        NormalizeEndpointIndices(
            projected,
            (canvas.Connections ?? Enumerable.Empty<GhJsonConnection>()).Concat(connectionAdds).Concat(connectionRemoves));

        var removedWires = new HashSet<string>(connectionRemoves.Select(ConnectionIdentity), StringComparer.Ordinal);
        List<GhJsonConnection> projectedConnections = (canvas.Connections ?? Enumerable.Empty<GhJsonConnection>())
            .Where(c => c.From is { } from
                && c.To is { } to
                && !removedIds.Contains(from.Id)
                && !removedIds.Contains(to.Id)
                && !removedWires.Contains(ConnectionIdentity(c)))
            .Concat(connectionAdds)
            .ToList();

        // Every id the patch's own connection ops name — the only ids whose endpoint validity this
        // patch is answerable for. A canvas wire between two untouched components is the canvas's
        // truth, not the model's authoring, and must never be reported.
        var patchEndpointIds = new HashSet<int>();
        foreach (GhJsonConnection conn in connectionAdds.Concat(connectionRemoves))
        {
            if (conn.From is { } from)
            {
                patchEndpointIds.Add(from.Id);
            }

            if (conn.To is { } to)
            {
                patchEndpointIds.Add(to.Id);
            }
        }

        // Ports the patch wires INTO, at port granularity for the multi-wire check (an item input
        // that already collected two user wires is not this patch's doing) and at component
        // granularity for the same-source check (the patch changed one operand of the pair).
        var wiredIntoPorts = new HashSet<string>(StringComparer.Ordinal);
        var wiredIntoIds = new HashSet<int>();
        foreach (GhJsonConnection conn in connectionAdds)
        {
            if (conn.To is not { } to)
            {
                continue;
            }

            wiredIntoIds.Add(to.Id);
            wiredIntoPorts.Add(PortKey(to.Id, to.ParamIndex));
        }

        // Components this patch just cut loose: it removed a wire running FROM them, or removed the
        // component that consumed their output. Those are the only orphans a patch can create, and
        // scoping to them is what keeps this off the user's pre-existing dangling objects.
        var strandedIds = new HashSet<int>();
        foreach (GhJsonConnection conn in connectionRemoves)
        {
            if (conn.From is { } from)
            {
                strandedIds.Add(from.Id);
            }
        }

        foreach (GhJsonConnection conn in canvas.Connections ?? Enumerable.Empty<GhJsonConnection>())
        {
            if (conn.To is { } to && removedIds.Contains(to.Id) && conn.From is { } from)
            {
                strandedIds.Add(from.Id);
            }
        }

        // Required-input findings stay scoped to ADDED components. An existing component can look
        // starved for a reason the export cannot show: the canvas state deliberately drops Physalia
        // objects and their wires, so a native input fed by a Physalia output reads as unwired. A
        // phantom here would hard-reject a submission the model cannot fix. Under-counting a wire
        // can never invent a multi-wire or same-source finding, so those need no such guard.
        return Messages(LintGraph(projected, projectedConnections, endpointIdsMustResolve: true)
            .Where(f => f.Kind switch
            {
                LintFindingKind.RequiredInput => addIds.Contains(f.SubjectId),
                LintFindingKind.MultiWireItem => addIds.Contains(f.SubjectId)
                    || wiredIntoPorts.Contains(PortKey(f.SubjectId, f.SubjectPort)),
                LintFindingKind.Endpoint => patchEndpointIds.Contains(f.SubjectId),
                LintFindingKind.Orphan => addIds.Contains(f.SubjectId)
                    || strandedIds.Contains(f.SubjectId)
                    || modifiedIds.Contains(f.SubjectId),
                LintFindingKind.SelfCombination => addIds.Contains(f.SubjectId) || wiredIntoIds.Contains(f.SubjectId),
                _ => false,
            }));
    }

    // What a finding is about, so the patch path can scope reporting per kind: a defect on a
    // component the patch never touched belongs to the user, not to the model.
    private enum LintFindingKind
    {
        RequiredInput,
        MultiWireItem,
        Endpoint,
        Orphan,
        SelfCombination,
    }

    // One defect: its kind, the component id it is about (an endpoint finding names the id its
    // connection referenced), the input index when the defect is port-specific, and the
    // model-facing message.
    private readonly record struct LintFinding(LintFindingKind Kind, int SubjectId, int? SubjectPort, string Message);

    // Findings → the deduplicated message lines the guardrail reports.
    private static IReadOnlyList<string> Messages(IEnumerable<LintFinding> findings) =>
        findings.Select(f => f.Message).Distinct(StringComparer.Ordinal).ToList();

    private static string PortKey(int id, int? paramIndex) =>
        $"{id}:{(paramIndex is int i ? i.ToString() : "?")}";

    /// <summary>
    /// Checks a graph for statically knowable wiring defects: required inputs with neither a wire
    /// nor an internalized value; multiple wires collecting into an item-access input (they build a
    /// list and multiply every downstream item); connection endpoints that reference a port the
    /// component does not have (placement would drop the wire); data-only components whose outputs
    /// nothing consumes (abandoned intent); and operators taking both operands from the same source
    /// port (a value combined with itself). Components whose type cannot be introspected are
    /// skipped (placement reports unknown components itself); the variable-parameter sentinel port
    /// is never required.
    /// </summary>
    /// <param name="components">The components to check (component-type guids already stamped).</param>
    /// <param name="connections">Every connection that could feed those components.</param>
    /// <param name="endpointIdsMustResolve">
    /// True when an endpoint id naming no known component is a defect. Always true on the
    /// full-document path and on the projected patch path; false only when a patch had no canvas to
    /// project onto and its adds are read in isolation.
    /// </param>
    /// <returns>One finding per defect; empty when the graph is clean.</returns>
    private static List<LintFinding> LintGraph(
        IEnumerable<GhJsonComponent> components,
        IEnumerable<GhJsonConnection> connections,
        bool endpointIdsMustResolve)
    {
        List<GhJsonComponent> componentList = components.ToList();
        List<GhJsonConnection> authored = connections.ToList();

        // Signature map for every introspectable component, plus every id an endpoint may
        // legitimately name (introspection failures included), so endpoint-id resolution is judged
        // separately from port-level checks.
        var sigById = new Dictionary<int, (GhJsonComponent Component, IReadOnlyList<ComponentPort> Inputs, IReadOnlyList<ComponentPort> Outputs)>();
        var componentById = new Dictionary<int, GhJsonComponent>();
        var resolvableIds = new HashSet<int>();
        foreach (GhJsonComponent component in componentList)
        {
            if (component.Id is not int id)
            {
                continue;
            }

            resolvableIds.Add(id);
            componentById[id] = component;
            if (component.ComponentGuid is Guid typeGuid
                && ComponentSignatureProvider.TryGetSignature(typeGuid, out IReadOnlyList<ComponentPort> ins, out IReadOnlyList<ComponentPort> outs))
            {
                sigById[id] = (component, ins, outs);
            }
        }

        // Address every port one way before counting anything: an endpoint authored by paramName
        // gets the index its component's signature gives that name. Without this, one wire authored
        // by index and another by name into the same item input read as one wire each.
        NormalizeEndpointIndices(componentList, authored);

        // Deduplicate identical wires before anything counts them. The same (source port → target
        // port) pair authored twice is ONE wire on the canvas — Grasshopper treats the repeat as a
        // no-op — so counting it twice used to report a phantom "receives 2 wires but consumes ONE
        // item" and push the model into inventing a component to combine a value with itself.
        List<GhJsonConnection> connectionList = authored
            .GroupBy(ConnectionIdentity, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        // Wire COUNTS per target component id, addressable by paramIndex and by name. Each
        // connection is counted exactly once — paramIndex preferred — so an endpoint authored
        // with both fields never double-counts. Source ids are collected for the orphan check, and
        // source→target groupings for the same-source check.
        var wiredIndices = new Dictionary<int, Dictionary<int, int>>();
        var wiredNames = new Dictionary<int, Dictionary<string, int>>();
        var consumedIds = new HashSet<int>();
        var feedsPerSource = new Dictionary<int, Dictionary<string, List<GhJsonConnection>>>();
        foreach (GhJsonConnection conn in connectionList)
        {
            if (conn.From is { } source)
            {
                consumedIds.Add(source.Id);
            }

            if (conn.To is not { } to)
            {
                continue;
            }

            if (conn.From is { } from)
            {
                Dictionary<string, List<GhJsonConnection>> bySource =
                    feedsPerSource.TryGetValue(to.Id, out Dictionary<string, List<GhJsonConnection>>? existingFeeds)
                        ? existingFeeds
                        : feedsPerSource[to.Id] = new Dictionary<string, List<GhJsonConnection>>(StringComparer.Ordinal);
                string sourceKey = EndpointKey(from);
                if (!bySource.TryGetValue(sourceKey, out List<GhJsonConnection>? feeds))
                {
                    bySource[sourceKey] = feeds = new List<GhJsonConnection>();
                }

                feeds.Add(conn);
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

        var violations = new List<LintFinding>();

        // Endpoint validity: a wire referencing a port the component does not have is dropped at
        // placement with a conflict the model never sees pre-emptively — e.g. authoring "from
        // output paramIndex 1" on a single-output component. Checked against the same signatures
        // the required-input pass trusts; variable-parameter components (trailing "…" sentinel)
        // skip bounds checks because their live port count can exceed the default signature.
        foreach (GhJsonConnection conn in connectionList)
        {
            if (conn.From is { } from)
            {
                LintEndpoint(from, output: true, sigById, resolvableIds, endpointIdsMustResolve, violations);
            }

            if (conn.To is { } target)
            {
                LintEndpoint(target, output: false, sigById, resolvableIds, endpointIdsMustResolve, violations);
            }
        }

        foreach (GhJsonComponent component in componentList)
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
                    violations.Add(new LintFinding(
                        LintFindingKind.MultiWireItem,
                        id,
                        i,
                        $"'{component.Name}' (id {id}) input '{port.Name}' (paramIndex {i}): {wireCount} wires into an item-access input collect into a {wireCount}-item list and everything downstream multiplies. Wire ONE source, or combine the values upstream (e.g. Addition)."));
                }

                if (!port.Required)
                {
                    continue;
                }

                bool internalized = (component.InputSettings ?? Enumerable.Empty<GhJsonParameterSettings>())
                    .Any(s => s.InternalizedData is not null && s.ParameterName == port.Name);

                if (wireCount == 0 && !internalized)
                {
                    violations.Add(new LintFinding(
                        LintFindingKind.RequiredInput,
                        id,
                        i,
                        $"'{component.Name}' (id {id}) input '{port.Name}' (paramIndex {i}): required, but has no wire and no internalized value — wire it or internalize one."));
                }
            }

            LintOrphan(component, id, sig.Outputs, inputs.Count, consumedIds, violations);
            LintSelfCombination(component, id, inputs, sigById, componentById, feedsPerSource, violations);
        }

        return violations;
    }

    /// <summary>
    /// Flags a component that produces ONLY data-typed outputs and feeds nothing — half an idea the
    /// model never wired in. This deliberately covers zero-input value sources: a Number Slider
    /// driving nothing is the same defect, and the blanket exemption for every floating param that
    /// used to sit here is exactly how a "Ridge Height" slider stayed unwired across a whole
    /// session while the model kept confirming it drove the roof (the ridge height was in fact
    /// reading a centroid coordinate, so the geometry moved for unrelated reasons and every report
    /// read as success). Exemptions are by KIND instead: annotation objects, which are notes rather
    /// than data, and Rhino-referenced params, which exist whether or not this graph consumes them.
    /// Geometry terminals are exempt via the output type hint — a Domain Box IS the result.
    /// </summary>
    /// <param name="component">The component being checked.</param>
    /// <param name="id">Its authored id.</param>
    /// <param name="outputs">Its introspected output ports.</param>
    /// <param name="inputCount">Its input count, which selects the wording (value source vs component).</param>
    /// <param name="consumedIds">Ids that at least one wire runs out of.</param>
    /// <param name="violations">The finding sink.</param>
    private static void LintOrphan(
        GhJsonComponent component,
        int id,
        IReadOnlyList<ComponentPort> outputs,
        int inputCount,
        HashSet<int> consumedIds,
        List<LintFinding> violations)
    {
        if (outputs.Count == 0
            || consumedIds.Contains(id)
            || IsAnnotationObject(component)
            || IsRhinoReferencedParam(component)
            || !outputs.All(o => IsDataOnlyHint(o.TypeHint)))
        {
            return;
        }

        string kinds = string.Join(", ", outputs.Select(o => o.TypeHint).Distinct());
        violations.Add(new LintFinding(
            LintFindingKind.Orphan,
            id,
            null,
            inputCount == 0
                ? $"{Describe(component, id)} is a value source nothing reads — wire its {kinds} output to the input it should drive, or remove it."
                : $"{Describe(component, id)} produces only data ({kinds}) and nothing consumes it — wire its result somewhere or remove it."));
    }

    /// <summary>
    /// Flags an operator whose operands all come from ONE source port. A+A, A−A, A÷A, min(A,A), a
    /// Line from a point to itself: legal wiring, degenerate meaning, and invisible to a solve —
    /// the graph runs clean and the model has no way to see that its second operand went missing.
    /// This is the shape a rewire leaves behind when one input is redirected and the other is
    /// forgotten (observed live: a Ridge Z Addition rewired to Wall Height on input A while Wall
    /// Height still fed input B, silently making the roof height twice the wall height).
    /// </summary>
    /// <param name="component">The component being checked.</param>
    /// <param name="id">Its authored id.</param>
    /// <param name="inputs">Its introspected input ports, for port labels.</param>
    /// <param name="sigById">Signatures by id, for the source component's port labels.</param>
    /// <param name="componentById">Components by id, for naming the shared source.</param>
    /// <param name="feedsPerSource">Per target id, the wires grouped by source port.</param>
    /// <param name="violations">The finding sink.</param>
    private static void LintSelfCombination(
        GhJsonComponent component,
        int id,
        IReadOnlyList<ComponentPort> inputs,
        Dictionary<int, (GhJsonComponent Component, IReadOnlyList<ComponentPort> Inputs, IReadOnlyList<ComponentPort> Outputs)> sigById,
        Dictionary<int, GhJsonComponent> componentById,
        Dictionary<int, Dictionary<string, List<GhJsonConnection>>> feedsPerSource,
        List<LintFinding> violations)
    {
        if (!SelfCombinationDegenerate.Contains(component.Name ?? string.Empty)
            || !feedsPerSource.TryGetValue(id, out Dictionary<string, List<GhJsonConnection>>? bySource))
        {
            return;
        }

        foreach (KeyValuePair<string, List<GhJsonConnection>> group in bySource)
        {
            List<GhJsonConnection> distinct = group.Value
                .GroupBy(c => EndpointKey(c.To), StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();

            if (distinct.Count < 2)
            {
                continue;
            }

            string ports = string.Join(" and ", distinct.Select(c => PortLabel(c.To, inputs)));
            GhJsonConnectionEndpoint? from = distinct[0].From;
            IReadOnlyList<ComponentPort> sourcePorts = from is not null && sigById.TryGetValue(from.Id, out var sourceSig)
                ? sourceSig.Outputs
                : Array.Empty<ComponentPort>();
            string sourceName = from is null
                ? "one source"
                : componentById.TryGetValue(from.Id, out GhJsonComponent? sourceComponent)
                    ? Describe(sourceComponent, from.Id)
                    : $"id {from.Id}";

            // Name the source PORT only when the source has more than one — "'Number Slider'
            // (id 103, 'Wall Height') output 'Number Slider' (paramIndex 0)" says nothing twice.
            string source = from is not null && sourcePorts.Count > 1
                ? $"{sourceName} output {PortLabel(from, sourcePorts)}"
                : sourceName;
            int? firstPort = distinct[0].To?.ParamIndex;

            violations.Add(new LintFinding(
                LintFindingKind.SelfCombination,
                id,
                firstPort,
                $"{Describe(component, id)} takes {ports} from the SAME source — {source}. That is degenerate (A−A=0, A÷A=1, min(A,A)=A); wire the operand that should differ to its intended source, or internalize a value on it."));
        }
    }

    /// <summary>
    /// Validates one connection endpoint against its component's authored signature: the id must
    /// resolve (full-document path only), and an authored paramIndex or paramName must name a
    /// port that exists on the referenced side.
    /// </summary>
    /// <param name="endpoint">The authored endpoint.</param>
    /// <param name="output">True when the endpoint is a FROM (output side); false for TO (input side).</param>
    /// <param name="sigById">Signatures of the introspectable authored components.</param>
    /// <param name="resolvableIds">Every id an endpoint may name: the authored components, plus the canvas's own on the patch path.</param>
    /// <param name="idsMustResolve">Whether an unresolved id is a defect (full-document path).</param>
    /// <param name="violations">The finding sink.</param>
    private static void LintEndpoint(
        GhJsonConnectionEndpoint endpoint,
        bool output,
        Dictionary<int, (GhJsonComponent Component, IReadOnlyList<ComponentPort> Inputs, IReadOnlyList<ComponentPort> Outputs)> sigById,
        HashSet<int> resolvableIds,
        bool idsMustResolve,
        List<LintFinding> violations)
    {
        if (!resolvableIds.Contains(endpoint.Id))
        {
            if (idsMustResolve)
            {
                violations.Add(new LintFinding(
                    LintFindingKind.Endpoint,
                    endpoint.Id,
                    null,
                    $"a connection references component id {endpoint.Id}, which is neither on the canvas nor added by this submission — add the missing component, correct the endpoint id, or remove the connection."));
            }

            return;
        }

        if (!sigById.TryGetValue(endpoint.Id, out var sig) || HasVariableParams(sig.Inputs))
        {
            return;
        }

        IReadOnlyList<ComponentPort> ports = output ? sig.Outputs : sig.Inputs;
        string side = output ? "output" : "input";

        // A floating parameter (Param_Geometry, Param_Number, …) introspects with no ports of its
        // own even though it accepts a wire on either side — it IS the port. With an empty list
        // every endpoint on it reads as out of bounds, and the message renders "its inputs are: "
        // with nothing after the colon, which is unactionable as well as wrong. Nothing can be
        // checked against an empty signature, so there is nothing to say.
        if (ports.Count == 0)
        {
            return;
        }

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
            violations.Add(new LintFinding(
                LintFindingKind.Endpoint,
                endpoint.Id,
                endpoint.ParamIndex,
                $"a connection references {authored} on '{sig.Component.Name}' (id {endpoint.Id}), but its {side}s are: {available} — fix the {(output ? "from" : "to")} endpoint."));
        }
    }

    /// <summary>
    /// Fills in each endpoint's paramIndex from its paramName wherever the referenced component's
    /// signature makes the index knowable, so every later pass addresses ports one way: wire
    /// counting, the duplicate-wire dedup, and a patch removal matched against the canvas export
    /// (which always writes both forms). An endpoint whose name matches no port keeps its null
    /// index and is reported by <see cref="LintEndpoint"/> instead of being silently rewritten.
    /// </summary>
    /// <param name="components">The components the endpoints may reference.</param>
    /// <param name="connections">The connections to normalize in place.</param>
    private static void NormalizeEndpointIndices(
        IEnumerable<GhJsonComponent> components,
        IEnumerable<GhJsonConnection> connections)
    {
        var portsById = new Dictionary<int, (IReadOnlyList<ComponentPort> Inputs, IReadOnlyList<ComponentPort> Outputs)>();
        foreach (GhJsonComponent component in components)
        {
            if (component.Id is int id
                && component.ComponentGuid is Guid typeGuid
                && ComponentSignatureProvider.TryGetSignature(typeGuid, out IReadOnlyList<ComponentPort> ins, out IReadOnlyList<ComponentPort> outs))
            {
                portsById[id] = (ins, outs);
            }
        }

        void Fill(GhJsonConnectionEndpoint? endpoint, bool output)
        {
            if (endpoint is null
                || endpoint.ParamIndex is not null
                || string.IsNullOrWhiteSpace(endpoint.ParamName)
                || !portsById.TryGetValue(endpoint.Id, out var sig))
            {
                return;
            }

            IReadOnlyList<ComponentPort> ports = output ? sig.Outputs : sig.Inputs;
            for (int i = 0; i < ports.Count; i++)
            {
                if (string.Equals(ports[i].Name, endpoint.ParamName, StringComparison.Ordinal))
                {
                    endpoint.ParamIndex = i;
                    return;
                }
            }
        }

        foreach (GhJsonConnection connection in connections)
        {
            Fill(connection.From, output: true);
            Fill(connection.To, output: false);
        }
    }

    // Identity of a wire: its two endpoints, each as id + port. An endpoint addressed by
    // paramIndex and the same endpoint addressed by paramName are not recognised as the same wire
    // — the lint would rather miss a duplicate than merge two distinct ports. NormalizeEndpointIndices
    // removes most of that gap by resolving names to indices wherever a signature is available.
    private static string ConnectionIdentity(GhJsonConnection connection) =>
        $"{EndpointKey(connection.From)}>{EndpointKey(connection.To)}";

    private static string EndpointKey(GhJsonConnectionEndpoint? endpoint) => endpoint is null
        ? "-"
        : $"{endpoint.Id}:{(endpoint.ParamIndex is int i ? i.ToString() : endpoint.ParamName ?? string.Empty)}";

    // A port as the model should read it back: name plus paramIndex when both are knowable.
    private static string PortLabel(GhJsonConnectionEndpoint? endpoint, IReadOnlyList<ComponentPort> ports)
    {
        if (endpoint is null)
        {
            return "an unknown port";
        }

        if (endpoint.ParamIndex is int index)
        {
            return index >= 0 && index < ports.Count
                ? $"'{ports[index].Name}' (paramIndex {index})"
                : $"paramIndex {index}";
        }

        return string.IsNullOrWhiteSpace(endpoint.ParamName) ? "an unnamed port" : $"'{endpoint.ParamName}'";
    }

    // A component as the model should read it back: type name, id, and the nickname it authored —
    // "'Number Slider' (id 202, 'Ridge Height')" is actionable where "'Number Slider' (id 202)"
    // makes the model go hunting through a canvas of identical sliders.
    private static string Describe(GhJsonComponent component, int id) =>
        !string.IsNullOrWhiteSpace(component.NickName)
        && !string.Equals(component.NickName, component.Name, StringComparison.Ordinal)
            ? $"'{component.Name}' (id {id}, '{component.NickName}')"
            : $"'{component.Name}' (id {id})";

    // The trailing "…" sentinel marks a variable-parameter component (Merge, Entwine, zui) whose
    // live port count can legitimately exceed the default signature.
    private static bool HasVariableParams(IReadOnlyList<ComponentPort> inputs) =>
        inputs.Any(p => p.Name == "…");

    // Canvas objects that carry text or decoration rather than data. A Panel wired to nothing is a
    // note the model wrote on purpose — the graphs it authors are full of them — so the orphan
    // check must never read one as abandoned intent. Matched by name because these are the same
    // names the model authors and the component catalog advertises.
    private static readonly HashSet<string> AnnotationObjectNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Panel", "Scribble", "Sketch", "Image", "Group", "Legend",
    };

    private static bool IsAnnotationObject(GhJsonComponent component) =>
        component.Name is { Length: > 0 } name && AnnotationObjectNames.Contains(name);

    // Params marked as referencing live Rhino geometry (the canvas-state export injects the key).
    // They are data sources that exist independently of this graph — the model is told to wire FROM
    // them, never to recreate them — so one the graph happens not to read is not abandoned intent.
    private static bool IsRhinoReferencedParam(GhJsonComponent component) =>
        component.ComponentState?.Extensions?.ContainsKey(RhinoRefExtensionKey) == true;

    // Operators for which every operand arriving from ONE source port is degenerate: the result is
    // a constant (A−A, A÷A, A=A), the operand itself (min/max), or a null object (a Line whose
    // start and end are the same point). Multiplication and Power are deliberately absent — A² is
    // a plausible authored intent, and a false positive here hard-rejects a submission. Matched by
    // name, like the annotation table: a same-named operator from another plug-in earning the same
    // scrutiny is an acceptable trade for not maintaining a guid list.
    private static readonly HashSet<string> SelfCombinationDegenerate = new(StringComparer.OrdinalIgnoreCase)
    {
        "Addition", "Subtraction", "Division", "Modulus",
        "Minimum", "Maximum", "Line", "Equality", "Similarity",
        "Larger Than", "Smaller Than",
    };

    // Output type hints that mean "this component's result IS the placed geometry" — legitimate
    // terminals the orphan check must never flag. Unknown/blank/Generic hints fail OPEN (treated
    // as possibly-geometry), mirroring the lint's skip-what-cannot-be-introspected policy.
    private static readonly HashSet<string> GeometryTypeHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "Point", "Line", "Curve", "Circle", "Arc", "Rectangle", "Polyline",
        "Surface", "Brep", "Mesh", "Box", "Geometry", "Extrusion", "SubD", "Group",
    };

    // Grasshopper reports an untyped output as "Generic Data", not "Generic" — the exemption used
    // to test only the latter, so every Merge / Entwine / generic-param terminal was permanently
    // flagged as an orphan. A generic output can be carrying breps for all this lint knows, so it
    // cannot be called data-only; observed live, it cost three consecutive rounds as the model
    // wired a terminal, was told to wire it somewhere, and tripped the same lint on whatever it
    // added next.
    private static readonly HashSet<string> GenericTypeHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "Generic", "Generic Data", "Data", "Object",
    };

    private static bool IsDataOnlyHint(string typeHint) =>
        !string.IsNullOrWhiteSpace(typeHint)
        && !GenericTypeHints.Contains(typeHint)
        && !GeometryTypeHints.Contains(typeHint);
}
