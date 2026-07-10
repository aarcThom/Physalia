// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.Core.Validation;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// A deterministic guardrail that inspects an LLM-generated payload (arriving as the consumed
/// signal's payload) for statically knowable input-wiring defects: a required input left with
/// neither a wire nor an internalized value, and multiple wires collecting into an item-access
/// input (they build a list and multiply every downstream item). Either would otherwise cost
/// whole solve-and-feedback rounds of "failed to collect data" warnings and degenerate geometry
/// once placed. A clean payload routes forward on the Success Signal unchanged (for the Component
/// Transmitter to place); defects route a crisp, actionable list back on the Fail Signal so the
/// model can correct and resubmit. Handles both a full GhJSON graph (every component) and a
/// ghpatch (its added components).
/// </summary>
public class RequiredInputCheck : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredInputCheck"/> class.
    /// </summary>
    public RequiredInputCheck()
        : base(
            "Required Input Check",
            "ReqIn",
            "Flags required inputs left with no wire and no internalized value before placement. A clean graph passes forward unchanged; unmet required inputs route a fix-it list back.",
            "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D2F5A9C4-6B31-4E0A-8C77-1A9E4F2B3D68");

    /// <inheritdoc/>
    /// <remarks>The GhJSON graph arrives as the consumed signal's payload.</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — the check is a pure static read; all work is in ReadSolve.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: the check has no side effects to push before reading.
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        IReadOnlyList<string> violations = GhJsonBridge.LintRequiredInputsJson(data);

        return violations.Count == 0
            ? RoutingResult.Ok(data)
            : RoutingResult.Fail(
                BuildFeedback(violations, GhPatchDetector.IsGhPatch(data)),
                "The submission has statically detectable input-wiring defects.",
                GH_RuntimeMessageLevel.Warning);
    }

    /// <summary>
    /// Builds the feedback payload routed back on the Fail Signal, listing every required input
    /// the model left with no wire and no internalized value. The wording leads with placement
    /// status — the submission was rejected BEFORE it reached the canvas, so the canvas is
    /// unchanged — and pins the resubmission mode, because a corrective turn that has to re-derive
    /// both from first principles wobbles between full-document and ghpatch.
    /// </summary>
    /// <param name="violations">One line per unmet required input.</param>
    /// <param name="isPatch">Whether the rejected submission was a ghpatch (vs a full document).</param>
    /// <returns>A model-facing feedback string.</returns>
    private static string BuildFeedback(IReadOnlyList<string> violations, bool isPatch)
    {
        var sb = new StringBuilder();
        sb.AppendLine(isPatch
            ? "The patch was NOT applied: it was rejected before it reached the canvas because added "
              + "components have input-wiring defects (a required input with no value, or multiple "
              + "wires collecting into a one-item input). The canvas is unchanged and the base "
              + "checksum you used is still valid. Resubmit the corrected ghpatch: fix ONLY the inputs "
              + "listed below and keep every other operation identical."
            : "Nothing was placed: your submission was rejected before it reached the canvas because "
              + "of the input-wiring defects listed below (a required input with no value, or multiple "
              + "wires collecting into a one-item input), so the canvas is unchanged from the state "
              + "you were shown. Resubmit your ENTIRE corrected full GhJSON document — do NOT switch "
              + "to a ghpatch; none of your components exist on the canvas yet. Fix ONLY the inputs "
              + "listed below and keep every other component and connection identical to your "
              + "previous submission.");

        foreach (string violation in violations)
        {
            sb.AppendLine($"  - {violation}");
        }

        return sb.ToString().TrimEnd();
    }
}
