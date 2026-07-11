// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.Core.Validation;

namespace Physalia.GH.Components;

/// <summary>
/// Presence gate that routes a response by whether it contains attempted JSON at all. A response
/// carrying any JSON — even malformed or truncated — passes through untouched on the Success
/// Signal so the Schema Validator can validate it and the correction loop keeps working. A response with
/// no JSON attempt (plain conversation) routes its raw text to the Fail Signal, which acts as a
/// quiet switch: left unwired, casual chat dead-ends there instead of triggering validation
/// feedback. Not a validator — well-formedness and schema checks stay in the Schema Validator.
/// </summary>
public class DetectJson : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DetectJson"/> class.
    /// </summary>
    public DetectJson()
        : base("Detect JSON", "DJson", "Passes responses containing JSON (even malformed) to Success; routes plain conversation to Fail so it never triggers validation feedback.", "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("85E51782-BA18-4B96-9488-B574950F2963");

    /// <inheritdoc/>
    /// <remarks>
    /// Accepts even a blank payload — unlike the Schema Validator's non-blank guard — because a blank
    /// response is still a real event and should route to Fail, not be dropped with a warning.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — no settle pass needed; all work is in ReadSolve.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: detection has no side effects to push before reading.
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The fail payload is the raw response text (not a feedback message), so the gate is a pure
    /// demultiplexer; the Remark level keeps the canvas quiet — routing chat to Fail is normal
    /// operation, not an error.
    /// </remarks>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        return JsonDetector.ContainsJson(data)
            ? RoutingResult.Ok(data)
            : RoutingResult.Fail(data, "No JSON detected in the response; routed to Fail.", GH_RuntimeMessageLevel.Remark);
    }
}
