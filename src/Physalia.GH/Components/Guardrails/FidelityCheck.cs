// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.Core.Validation;
using Physalia.GH.Generation;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// A deterministic guardrail that verifies the graph the Component Transmitter placed matches the
/// definition the model authored: every component landed and every connection exists on the live
/// canvas. This is the intent-vs-realization diff a runtime scan cannot do — a failed wire into an
/// optional input produces no error, no warning, and no dead component, yet the canvas silently
/// differs from what the model believes it built. The trigger signal is the transmitter's Success
/// Signal (payload = the placed instanceGuids); the authored definition arrives on the typed
/// Definition input, wired from the SAME signal the transmitter consumed (a Signal wire plugged
/// into a text input yields its payload, read passively — it never triggers a run). A faithful
/// placement forwards the placed GUIDs unchanged (for a Runtime Health Check to scan);
/// discrepancies route an itemised fix-it list back on the Fail Signal. Ghpatch turns pass
/// through — fidelity verification covers full-graph placements only; the transmitter's own
/// per-operation conflict reporting covers patches.
///
/// <para>A MISCONFIGURED check (Definition unwired, blank, or reading text that does not parse —
/// which can never be the model's definition, since the definition that placed always parses)
/// never routes the mis-wire into the loop: the model is powerless to fix the user's wiring.
/// The check first falls back to the definition the placement itself recorded (available only
/// when the trigger's guids are exactly that placement's — an applied patch invalidates it), so
/// a mis-wired rig still verifies fidelity with a warning naming the wiring problem. Only when
/// no recorded definition covers the turn does it surface a persistent local Error bubble and
/// pass the trigger through unverified, so the Runtime Health Check still runs and the human
/// sees exactly what to rewire.</para>
/// </summary>
public class FidelityCheck : RoutingComponentBase<string>
{
    private const int DefinitionInputIndex = 0;

    // The authored definition, captured at consume time — by the read pass an upstream re-latch
    // could theoretically have replaced the wire's payload. Session-only, like all lifecycle state.
    private string _definition = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="FidelityCheck"/> class.
    /// </summary>
    public FidelityCheck()
        : base(
            "Fidelity Check",
            "Fidelity",
            "Compares the canvas against the definition that produced it — every component landed, every connection made. It catches the gap between what the model asked for and what actually appeared. Whole definitions only; an edit to an existing one passes straight through.",
            "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("E1B7A4C9-3D58-4F02-B6A1-9C24E8D57F13");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The finished placement to verify. Wire a Component Transmitter's Success Signal, which carries the ids of what it placed.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "Those same ids, passed on when the canvas matches the definition. Wire into a Runtime Health Check or a Geometry Report.";

    /// <inheritdoc/>
    protected override string FailSignalDescription =>
        "What did not survive placement, component by component and connection by connection. Wire into a Feedback.";

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        int index = pManager.AddTextParameter(
            "Definition",
            "D",
            "The definition that was placed, to compare the canvas against. Wire it from the same signal the Component Transmitter runs on: a signal wire plugged into this text input just hands over its text and starts nothing. Leave it unwired and the check uses the definition recorded at placement time.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[index].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The trigger payload is the transmitter's placed GUIDs; the authored definition is captured
    /// from the typed input in the same solve. A blank GUID payload still runs the check — an
    /// empty scope must fail loudly (every authored component reads as missing), not stall the
    /// chain with a silently dropped signal.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        string definition = string.Empty;
        da.GetData(DefinitionInputIndex, ref definition);
        _definition = definition ?? string.Empty;

        data = signal.Payload ?? string.Empty;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — the diff reads live structure only; all work is in ReadSolve.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: verification has no side effects to push before reading.
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        List<Guid> placed = ParseGuids(data).ToList();

        string definition = _definition;
        string? fallbackNote = null;

        if (string.IsNullOrWhiteSpace(definition))
        {
            // Before declaring the check unrunnable, try the definition the placement itself
            // recorded — available only when the trigger's guids are exactly that placement's, so
            // the fallback can never verify a stale turn (any applied patch invalidates it).
            bool unwired = Params.Input[DefinitionInputIndex].SourceCount == 0;
            string wiringProblem = unwired ? "Definition input is unwired" : "Definition input is blank";

            definition = GhJsonBridge.TryGetAuthoredDefinition(PhyDocuments.Host(this), placed) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(definition))
            {
                // No recorded definition for this turn either. A wiring problem is the USER's
                // problem — the model is powerless to fix it, so routing it into the loop burns a
                // turn on a relay and dead-ends the chain. Surface it as a persistent local Error
                // and pass the trigger through so the Runtime Health Check still runs. Fidelity is
                // NOT verified this turn — the red bubble guards against a silent pass.
                return RoutingResult.Ok(
                    data,
                    message: wiringProblem
                        + " and no recorded placement definition matches this turn — fidelity NOT verified. "
                        + "Wire Definition to the same signal wire the Component Transmitter's Signal input "
                        + "consumes. Passed through so downstream checks still run.",
                    level: GH_RuntimeMessageLevel.Error);
            }

            fallbackNote = wiringProblem + " — verified against the definition recorded at placement instead. "
                + "Wire Definition to the same signal wire the Component Transmitter's Signal input consumes.";
        }

        if (GhPatchDetector.IsGhPatch(definition))
        {
            return RoutingResult.Ok(
                data,
                message: "Patch turn — fidelity verification covers full-graph placements only; passed through.",
                level: GH_RuntimeMessageLevel.Remark);
        }

        GhJsonBridge.FidelityReport report = GhJsonBridge.VerifyPlacementFidelity(definition, placed, PhyDocuments.Host(this), PhyDocuments.Harness(this));

        if (report.Misconfiguration is not null && fallbackNote is null
            && GhJsonBridge.TryGetAuthoredDefinition(PhyDocuments.Host(this), placed) is { } recorded)
        {
            // The wired Definition reads something that is not GhJSON (the classic mis-wire: a
            // markdown output). The placement's own record covers this turn — verify against it
            // and keep the mis-wire visible as a warning instead of skipping the check.
            fallbackNote = report.Misconfiguration
                + " Verified against the definition recorded at placement instead.";
            report = GhJsonBridge.VerifyPlacementFidelity(
                recorded, placed, PhyDocuments.Host(this), PhyDocuments.Harness(this));
        }

        if (report.Misconfiguration is not null)
        {
            // Same policy as the unwired case: never the model's fault, so never model feedback.
            return RoutingResult.Ok(
                data,
                message: report.Misconfiguration + " Passed through so downstream checks still run.",
                level: GH_RuntimeMessageLevel.Error);
        }

        return report.Violations.Count == 0
            ? RoutingResult.Ok(data, message: fallbackNote, level: GH_RuntimeMessageLevel.Warning)
            : RoutingResult.Fail(
                BuildFeedback(report),
                fallbackNote is null
                    ? $"{report.Violations.Count} fidelity problem(s) found."
                    : $"{report.Violations.Count} fidelity problem(s) found. {fallbackNote}",
                GH_RuntimeMessageLevel.Warning);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _definition = string.Empty;
    }

    /// <summary>
    /// Builds the feedback payload routed back on the Fail Signal, listing each discrepancy
    /// between the authored definition and the live canvas.
    /// </summary>
    /// <param name="report">The fidelity report carrying the violations and fresh checksum.</param>
    /// <returns>A model-facing feedback string.</returns>
    private static string BuildFeedback(GhJsonBridge.FidelityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Your definition was placed, but the live canvas does not match what you authored. Each "
            + "discrepancy below names the component by name, id (your numbering), and instanceGuid. "
            + "Fix ONLY the discrepancies — everything else landed correctly — and resubmit.");
        sb.AppendLine();

        foreach (string violation in report.Violations)
        {
            sb.AppendLine($"  - {violation}");
        }

        if (!string.IsNullOrEmpty(report.BaseChecksum))
        {
            sb.AppendLine();
            sb.AppendLine("Current base checksum — copy this verbatim into patch.base.checksum: " + report.BaseChecksum);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Parses newline-separated GUIDs from the payload, skipping any line that is not a GUID.
    /// </summary>
    /// <param name="payload">The incoming signal payload.</param>
    /// <returns>The GUIDs found in the payload.</returns>
    private static IEnumerable<Guid> ParseGuids(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        foreach (string line in payload.Split('\n'))
        {
            if (Guid.TryParse(line.Trim(), out Guid guid))
            {
                yield return guid;
            }
        }
    }
}
