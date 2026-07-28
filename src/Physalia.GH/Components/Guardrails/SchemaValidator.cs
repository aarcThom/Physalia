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

            Result<string, ValidationError>.Err err => RoutingResult.Fail(
                BuildFeedback(err.Error), err.Error.Message, GH_RuntimeMessageLevel.Warning),
            _ => RoutingResult.Fail("Unknown validation result."),
        };
    }

    private string BuildFeedback(ValidationError error)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Your previous response failed validation and was rejected before any transmitter acted "
            + "on it — nothing was placed or changed. Resubmit your ENTIRE corrected response in the "
            + "SAME document kind as before: fix ONLY the violations listed below and keep everything "
            + "else identical.");
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
            "Your previous response was CUT OFF at the token limit in the middle of the JSON "
            + "document (an opening brace is never closed), so it was rejected before any "
            + "transmitter acted on it — nothing was placed or changed. This is a length problem, "
            + "not a content problem: do not restructure the document. Re-send your ENTIRE "
            + "response as one complete JSON document, keeping any reasoning brief so the full "
            + "document fits within the response limit.");

        AppendFreshChecksum(sb);
        return sb.ToString().TrimEnd();
    }

    private string BuildMalformedFeedback()
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Your previous response is not parseable JSON and was rejected before any transmitter "
            + "acted on it — nothing was placed or changed. The document's brackets do not pair up: "
            + "somewhere a closing brace or bracket is missing (a closer arrives that matches the "
            + "wrong opener), so the document ends up one level short. The response was NOT cut off "
            + "— it reached its end — and the CONTENT is not in question: no schema violation is "
            + "being reported, because the document could not be read far enough to check one.");
        sb.AppendLine();
        sb.AppendLine(
            "Re-send the SAME document with balanced brackets. Walk it once from the top counting "
            + "depth, and pay particular attention to the deepest nesting — the internalizedData "
            + "objects and the componentState.extensions blocks are three and four levels deep, and "
            + "that is where a closer goes missing. Do not restructure anything else.");

        AppendFreshChecksum(sb);
        return sb.ToString().TrimEnd();
    }

    // The model may have applied a patch on an earlier turn, making its remembered base
    // checksum stale; carry the fresh one so the corrected resubmission cannot mismatch.
    private void AppendFreshChecksum(StringBuilder sb)
    {
        if (OnPingDocument() is { } doc
            && Generation.GhJsonBridge.TryExportCanvasState(doc)?.Checksum is { Length: > 0 } checksum)
        {
            sb.AppendLine();
            sb.AppendLine("Current base checksum — copy this verbatim into patch.base.checksum: " + checksum);
        }
    }
}
