// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// A deterministic guardrail that checks an LLM-generated payload (arriving as the consumed
/// signal's payload) against the GhJSON library's own validity verdict: parse, embedded
/// JSON-schema conformance, and structural integrity — unique component ids, connection
/// references that resolve, valid group membership, and for a ghpatch the patch schema plus
/// no instanceGuid on added components. This is cross-field validation the Schema Validator's
/// JSON Schema cannot express. A valid payload routes forward on the Success Signal unchanged;
/// an invalid one routes the library's verdict back on the Fail Signal so the model can correct
/// and resubmit. Unlike the Required Input Check, malformed JSON fails here — validity is this
/// node's entire purpose.
/// </summary>
public class GhDefinitionValidator : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GhDefinitionValidator"/> class.
    /// </summary>
    public GhDefinitionValidator()
        : base(
            "GH Definition Validator",
            "DefValid",
            "Validates a generated GhJSON graph or ghpatch against the GhJSON library's schema and structural-integrity checks (unique ids, connection references, group membership). A valid payload passes forward unchanged; an invalid one routes the verdict back.",
            "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7C4E8D2A-95B1-4F63-A80E-3D6F1B7C42E9");

    /// <inheritdoc/>
    /// <remarks>The GhJSON graph or ghpatch arrives as the consumed signal's payload.</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — the check is a pure static read; all work is in ReadSolve.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: validation has no side effects to push before reading.
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        return GhJsonBridge.ValidateDefinitionJson(data, out bool isPatch, out string? message)
            ? RoutingResult.Ok(data)
            : RoutingResult.Fail(
                BuildFeedback(isPatch, message),
                isPatch ? "Not a valid ghpatch." : "Not valid GhJSON.",
                GH_RuntimeMessageLevel.Warning);
    }

    /// <summary>
    /// Builds the feedback payload routed back on the Fail Signal, quoting the GhJSON library's
    /// validation verdict line by line.
    /// </summary>
    /// <param name="isPatch">Whether the payload was checked as a ghpatch (vs a full document).</param>
    /// <param name="message">The library's validation failure message.</param>
    /// <returns>A model-facing feedback string.</returns>
    private static string BuildFeedback(bool isPatch, string? message)
    {
        var sb = new StringBuilder();
        sb.AppendLine(isPatch
            ? "Your response is not a valid ghpatch, so it was NOT applied — the canvas is unchanged "
              + "and your base checksum is still valid. Fix ONLY the problems below and resubmit the "
              + "corrected ghpatch, keeping every other operation identical."
            : "Your response is not a valid GhJSON document, so nothing was placed — the canvas is "
              + "unchanged. Fix ONLY the problems below and resubmit your ENTIRE corrected full "
              + "document (do NOT switch to a ghpatch; none of your components exist yet), keeping "
              + "everything else identical.");

        if (!string.IsNullOrWhiteSpace(message))
        {
            sb.AppendLine();
            foreach (string line in message!.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    sb.AppendLine($"  - {trimmed}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }
}
