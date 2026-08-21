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
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// A deterministic guardrail that inspects an LLM-generated payload (arriving as the consumed
/// signal's payload) for statically knowable wiring defects: a required input left with neither a
/// wire nor an internalized value, multiple wires collecting into an item-access input (they build
/// a list and multiply every downstream item), a component whose outputs nothing consumes, and an
/// operator taking both operands from one source port. The first two would cost whole
/// solve-and-feedback rounds of "failed to collect data" warnings and degenerate geometry; the last
/// two a solve can NEVER report — the graph runs clean and produces geometry, just not the geometry
/// the model believes it built. A clean payload routes forward on the Success Signal unchanged (for
/// the Component Transmitter to place); defects route a crisp, actionable list back on the Fail
/// Signal so the model can correct and resubmit. Handles both a full GhJSON graph (every component)
/// and a ghpatch (the graph the patch would produce, scoped to what the patch touches).
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
            "Catches the wiring mistakes that can be seen before anything is placed: a required input with nothing in it, several wires into an input that takes one thing, a connection pointing past the end of a component, a slider driving nothing.",
            "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D2F5A9C4-6B31-4E0A-8C77-1A9E4F2B3D68");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The definition to inspect. Wire a GH Definition Validator's Success Signal, so the structure is already known to be sound.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "The definition passed through unchanged, once nothing is left dangling. Wire on to a Component Transmitter.";

    /// <inheritdoc/>
    protected override string FailSignalDescription =>
        "Every gap found, naming the component and the input, so the model knows exactly what to fill in. Wire into a Feedback.";

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
        IReadOnlyList<string> violations = GhJsonBridge.LintRequiredInputsJson(data, PhyDocuments.Harness(this));

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
            ? "The patch was NOT applied: the graph it would produce has the wiring defects below, "
              + "each on a component this patch adds or rewires (never pre-existing canvas work). "
              + "The canvas is unchanged and your base checksum is still valid. Fix ONLY these "
              + "defects and resubmit the corrected ghpatch, keeping every other operation identical."
            : "Nothing was placed: your submission has the wiring defects below, so the canvas is "
              + "unchanged. Fix ONLY these defects and resubmit your ENTIRE corrected full document "
              + "(do NOT switch to a ghpatch; none of your components exist yet), keeping everything "
              + "else identical.");

        foreach (string violation in violations)
        {
            sb.AppendLine($"  - {violation}");
        }

        return sb.ToString().TrimEnd();
    }
}
