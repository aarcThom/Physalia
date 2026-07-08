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
            Result<string, ValidationError>.Err err => RoutingResult.Fail(
                BuildFeedback(err.Error), err.Error.Message, GH_RuntimeMessageLevel.Warning),
            _ => RoutingResult.Fail("Unknown validation result."),
        };
    }

    private string BuildFeedback(ValidationError error)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Your previous response failed validation. Please correct and resubmit.");
        sb.AppendLine();
        sb.AppendLine($"Error: {error.Message}");

        if (error.Violations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Violations:");
            foreach (var v in error.Violations)
                sb.AppendLine($"  - {v.Path}: {v.Message}");
        }

        // The model may have applied a patch on an earlier turn, making its remembered base
        // checksum stale; carry the fresh one so the corrected resubmission cannot mismatch.
        if (OnPingDocument() is { } doc
            && Generation.GhJsonBridge.TryExportCanvasState(doc)?.Checksum is { Length: > 0 } checksum)
        {
            sb.AppendLine();
            sb.AppendLine("Current base checksum — copy this verbatim into patch.base.checksum: " + checksum);
        }

        return sb.ToString().TrimEnd();
    }
}
