// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.Core.Validation;

namespace Physalia.GH.Components;

/// <summary>
/// Strips LLM prose from the consumed signal's payload, validates the extracted JSON
/// against the provided schema, and routes the clean JSON forward on the Success Signal
/// or the validation feedback back on the Fail Signal.
/// </summary>
public class SchemaValidator : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaValidator"/> class.
    /// </summary>
    public SchemaValidator()
        : base("Schema Validator", "Schema Validator", "Strips LLM prose, validates JSON against schema, and passes clean output forward.", "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F3A8C21D-7E04-4B69-A953-D60F2E8B1C47");

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Schema", "Sc", "JSON schema string from System Prompt.", GH_ParamAccess.item, string.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>The raw LLM output arrives as the consumed signal's payload (LLM Call's Success Signal).</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — no settle pass needed; all work is in ReadSolve.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: validation has no side effects to push before reading.
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        string schema = string.Empty;
        da.GetData(0, ref schema);

        string extracted = JsonExtractor.ExtractJson(data);

        if (string.IsNullOrWhiteSpace(schema))
        {
            // No schema — pass extracted JSON through without validation.
            return RoutingResult.Ok(extracted);
        }

        return Physalia.Core.Validation.SchemaValidator.Validate(extracted, schema) switch
        {
            Result<string, ValidationError>.Ok ok => RoutingResult.Ok(JsonExtractor.PrettyPrint(ok.Value)),

            // A response cut off at the token limit leaves an unclosed JSON document; the
            // extractor then recovers some inner fragment, and validating THAT against the
            // document schema yields nonsense violations ("property 'name' is not allowed at
            // the document root"). Say what actually happened instead of relaying them.
            Result<string, ValidationError>.Err when JsonExtractor.LooksTruncated(data) => RoutingResult.Fail(
                BuildTruncationFeedback(),
                "The response was cut off mid-JSON (unclosed structure) — validation feedback replaced with a truncation notice.",
                GH_RuntimeMessageLevel.Warning),

            // A document the model finished but whose brackets do not pair up — one dropped
            // closing brace. The extractor cannot recover it, so whatever schema violations follow
            // describe a fragment rather than the document, and relaying them sends the model
            // hunting for a defect that is not there. Name the real one.
            Result<string, ValidationError>.Err when JsonExtractor.LooksMalformed(data) => RoutingResult.Fail(
                BuildMalformedFeedback(),
                "The JSON document's brackets do not balance — validation feedback replaced with a malformed-JSON notice.",
                GH_RuntimeMessageLevel.Warning),

            // A document whose ONLY fault is properties the schema does not allow is repairable
            // here: deleting them yields the document the model meant to send. Bouncing it back
            // instead costs a full resubmission — measured live at ~12,000 characters re-sent
            // verbatim to delete one invented key — and teaches the model nothing it can act on.
            Result<string, ValidationError>.Err err when TryRepair(err.Error, extracted, schema, out string repaired, out string note)
                => RoutingResult.Ok(repaired, message: note, level: GH_RuntimeMessageLevel.Remark),

            Result<string, ValidationError>.Err err => RoutingResult.Fail(
                BuildFeedback(err.Error), err.Error.Message, GH_RuntimeMessageLevel.Warning),
            _ => RoutingResult.Fail("Unknown validation result."),
        };
    }

    /// <summary>
    /// Attempts to make a failing document valid by deleting disallowed properties, and accepts
    /// the result only if it then validates cleanly.
    /// </summary>
    /// <param name="error">The reported validation error.</param>
    /// <param name="extracted">The extracted JSON that failed.</param>
    /// <param name="schema">The schema to re-validate against.</param>
    /// <param name="repaired">Receives the pretty-printed repaired document on success.</param>
    /// <param name="note">Receives the canvas remark naming what was dropped.</param>
    /// <returns>True when the document was repaired and now validates.</returns>
    private static bool TryRepair(
        ValidationError error,
        string extracted,
        string schema,
        out string repaired,
        out string note)
    {
        repaired = string.Empty;
        note = string.Empty;

        if (SchemaRepair.DropDisallowedProperties(extracted, error.Violations) is not { } outcome)
        {
            return false;
        }

        // Re-validation is the whole safety argument: the repair is only trusted if the schema
        // itself now accepts the document. Anything short of clean goes back to the model.
        if (Physalia.Core.Validation.SchemaValidator.Validate(outcome.Json, schema)
            is not Result<string, ValidationError>.Ok ok)
        {
            return false;
        }

        repaired = JsonExtractor.PrettyPrint(ok.Value);
        note = "Dropped "
            + string.Join(", ", outcome.RemovedPaths)
            + " — properties the schema does not allow, removed here instead of costing a resubmission.";
        return true;
    }

    private string BuildFeedback(ValidationError error)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Your response failed schema validation — nothing was placed or changed. Fix ONLY the "
            + "violations below and resubmit your ENTIRE response in the SAME document kind, keeping "
            + "everything else identical.");
        sb.AppendLine();
        sb.AppendLine($"Error: {error.Message}");

        if (error.Violations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Violations:");
            foreach (var v in error.Violations)
                sb.AppendLine($"  - {v.Path}: {v.Message}");
        }

        AppendFreshChecksum(sb);
        return sb.ToString().TrimEnd();
    }

    private string BuildTruncationFeedback()
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Your response was CUT OFF at the token limit mid-JSON — nothing was placed or changed. "
            + "This is a length problem, not a content problem: do not restructure the document. "
            + "Re-send your ENTIRE response as one complete JSON document, keeping any reasoning "
            + "brief so it fits.");

        AppendFreshChecksum(sb);
        return sb.ToString().TrimEnd();
    }

    private string BuildMalformedFeedback()
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Your response is not parseable JSON — a closing brace or bracket is missing somewhere — "
            + "so nothing was placed or changed. It was NOT cut off, and no schema violation is being "
            + "reported: the document simply could not be read. Re-send the SAME document with "
            + "balanced brackets — the deepest nesting (internalizedData, componentState.extensions) "
            + "is where a closer usually goes missing. Do not restructure anything else.");

        AppendFreshChecksum(sb);
        return sb.ToString().TrimEnd();
    }

    // The model may have applied a patch on an earlier turn, making its remembered base
    // checksum stale; carry the fresh one so the corrected resubmission cannot mismatch.
    private void AppendFreshChecksum(StringBuilder sb)
    {
        if (OnPingDocument() is { } doc
            && Generation.GhJsonBridge.CurrentBaseChecksum(doc) is { Length: > 0 } checksum)
        {
            sb.AppendLine();
            sb.AppendLine("Current base checksum — copy this verbatim into patch.base.checksum: " + checksum);
        }
    }
}
