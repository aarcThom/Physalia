// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Validation;

namespace Physalia.GH.Components;

/// <summary>
/// Strips LLM prose, validates the extracted JSON against the provided schema,
/// and routes output forward on success or back via Feedback on failure.
/// </summary>
public class Auditor : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Auditor"/> class.
    /// </summary>
    public Auditor()
        : base("Auditor", "Aud", "Strips LLM prose, validates JSON against schema, and passes clean output forward.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F3A8C21D-7E04-4B69-A953-D60F2E8B1C47");

    /// <inheritdoc/>
    protected override void RegisterDataInput(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Data", "D", "Raw LLM output from Reasoner.", GH_ParamAccess.item, string.Empty);
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Schema", "S", "JSON schema string from Composer.", GH_ParamAccess.item, string.Empty);
    }

    /// <inheritdoc/>
    protected override bool TryGetData(IGH_DataAccess da, out string data)
    {
        data = string.Empty;
        da.GetData(0, ref data);
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
        da.GetData(1, ref schema);

        string extracted = JsonExtractor.ExtractJson(data);

        if (string.IsNullOrWhiteSpace(schema))
        {
            // No schema — pass extracted JSON through without validation.
            return RoutingResult.Ok(extracted);
        }

        return SchemaValidator.Validate(extracted, schema) switch
        {
            Result<string, ValidationError>.Ok ok => RoutingResult.Ok(JsonExtractor.PrettyPrint(ok.Value)),
            Result<string, ValidationError>.Err err => RoutingResult.Fail(
                BuildFeedback(err.Error), err.Error.Message, GH_RuntimeMessageLevel.Warning),
            _ => RoutingResult.Fail("Unknown validation result."),
        };
    }

    private static string BuildFeedback(ValidationError error)
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

        return sb.ToString().TrimEnd();
    }
}
