// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// A deterministic guardrail that inspects an LLM-generated payload (arriving as the consumed
/// signal's payload) for required inputs left with neither a wire nor an internalized value — a
/// statically knowable defect that would otherwise cost a whole solve-and-feedback round of
/// "failed to collect data" warnings once placed. A clean payload routes forward on the Success
/// Signal unchanged (for the Component Transmitter to place); any unmet required inputs route a
/// crisp, actionable list back on the Fail Signal so the model can correct and resubmit. Handles
/// both a full GhJSON graph (every component) and a ghpatch (its added components).
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
            : RoutingResult.Fail(BuildFeedback(violations), "Required inputs have no value.", GH_RuntimeMessageLevel.Warning);
    }

    /// <summary>
    /// Builds the feedback payload routed back on the Fail Signal, listing every required input
    /// the model left with no wire and no internalized value.
    /// </summary>
    /// <param name="violations">One line per unmet required input.</param>
    /// <returns>A model-facing feedback string.</returns>
    private static string BuildFeedback(IReadOnlyList<string> violations)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Required inputs have no value. Wire each one or internalize a value, then resubmit "
            + "the corrected submission.");

        foreach (string violation in violations)
        {
            sb.AppendLine($"  - {violation}");
        }

        return sb.ToString().TrimEnd();
    }
}
