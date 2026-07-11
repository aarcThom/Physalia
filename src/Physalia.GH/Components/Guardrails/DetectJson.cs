// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.Core.Validation;

namespace Physalia.GH.Components;

/// <summary>
/// Presence gate that passes a response through only when it contains attempted JSON at all. A
/// response carrying any JSON — even malformed or truncated — passes through untouched on the
/// single Signal output so the Schema Validator can validate it and the correction loop keeps
/// working. A response with no JSON attempt (plain conversation) dead-ends quietly inside the
/// component: the state shows the swallowed event but nothing fires downstream, so casual chat
/// never triggers validation feedback. Not a validator — well-formedness and schema checks stay
/// in the Schema Validator.
/// </summary>
public class DetectJson : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DetectJson"/> class.
    /// </summary>
    public DetectJson()
        : base("Detect JSON", "DJson", "Passes responses containing JSON (even malformed) through; plain conversation dead-ends quietly so it never triggers validation feedback.", "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("85E51782-BA18-4B96-9488-B574950F2963");

    /// <inheritdoc/>
    /// <remarks>
    /// A single Signal output: the gate either passes the response through or swallows it, so a
    /// separate Fail output would only ever duplicate the same raw text.
    /// </remarks>
    protected override bool HasFailOutput => false;

    /// <inheritdoc/>
    /// <remarks>
    /// Accepts even a blank payload — unlike the Schema Validator's non-blank guard — because a blank
    /// response is still a real event and should be swallowed as non-JSON, not dropped with a warning.
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
    /// The non-JSON case is a quiet failure (no signal minted): the event dead-ends here by
    /// design, visible only as the Failed caption and a Remark — swallowing chat is normal
    /// operation, not an error.
    /// </remarks>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        return JsonDetector.ContainsJson(data)
            ? RoutingResult.Ok(data)
            : RoutingResult.Fail(data, "No JSON detected in the response; nothing passed through.", GH_RuntimeMessageLevel.Remark, emitSignal: false);
    }
}
