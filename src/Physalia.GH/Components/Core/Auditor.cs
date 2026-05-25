// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Validation;

namespace Physalia.GH.Components;

/// <summary>
/// Strips LLM prose, validates the extracted JSON against the provided schema,
/// and routes output forward on success or back via Feedback on failure.
/// </summary>
public class Auditor : PhyBase
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
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Data", "D", "Raw LLM output from Reasoner.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Schema", "S", "JSON schema string from Composer.", GH_ParamAccess.item, string.Empty);
        pManager.AddBooleanParameter("Trigger", "T", "Trigger from Reasoner.", GH_ParamAccess.item, false);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Data", "D", "Validated, formatted JSON string. Empty on failure.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Success Trigger", "ST", "Fires true when JSON is extracted and validates against the schema.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Fail Trigger", "FT", "Fires true when validation fails.", GH_ParamAccess.item);
        pManager.AddTextParameter("Feedback", "F", "Error message for routing back to Reasoner on failure. Empty on success.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string raw = string.Empty;
        string schema = string.Empty;
        bool trigger = false;

        DA.GetData(0, ref raw);
        DA.GetData(1, ref schema);
        if (!DA.GetData(2, ref trigger)) return;

        if (!trigger)
        {
            DA.SetData(0, string.Empty);
            DA.SetData(1, false);
            DA.SetData(2, false);
            DA.SetData(3, string.Empty);
            return;
        }

        string extracted = ExtractJson(raw);

        if (string.IsNullOrWhiteSpace(schema))
        {
            // No schema — pass extracted JSON through without validation.
            DA.SetData(0, extracted);
            DA.SetData(1, true);
            DA.SetData(2, false);
            DA.SetData(3, string.Empty);
            return;
        }

        var result = SchemaValidator.Validate(extracted, schema);

        switch (result)
        {
            case Result<string, ValidationError>.Ok ok:
                DA.SetData(0, PrettyPrint(ok.Value));
                DA.SetData(1, true);
                DA.SetData(2, false);
                DA.SetData(3, string.Empty);
                break;

            case Result<string, ValidationError>.Err err:
                string feedback = BuildFeedback(err.Error, extracted);
                DA.SetData(0, string.Empty);
                DA.SetData(1, false);
                DA.SetData(2, true);
                DA.SetData(3, feedback);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, err.Error.Message);
                break;
        }
    }

    /// <summary>
    /// Strips prose and markdown fences from raw LLM output, returning the embedded JSON.
    /// Falls back to returning the trimmed input unchanged if no JSON structure is found.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <returns>Extracted JSON string.</returns>
    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        // Try ```json ... ``` fence.
        int fenceStart = raw.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart >= 0)
        {
            int newline = raw.IndexOf('\n', fenceStart);
            if (newline >= 0)
            {
                int fenceEnd = raw.IndexOf("```", newline + 1);
                if (fenceEnd > newline)
                    return raw.Substring(newline + 1, fenceEnd - newline - 1).Trim();
            }
        }

        // Try generic ``` ... ``` fence.
        fenceStart = raw.IndexOf("```");
        if (fenceStart >= 0)
        {
            int newline = raw.IndexOf('\n', fenceStart);
            if (newline >= 0)
            {
                int fenceEnd = raw.IndexOf("```", newline + 1);
                if (fenceEnd > newline)
                    return raw.Substring(newline + 1, fenceEnd - newline - 1).Trim();
            }
        }

        // Find outermost { ... } or [ ... ].
        int objStart = raw.IndexOf('{');
        int arrStart = raw.IndexOf('[');

        if (objStart < 0 && arrStart < 0)
            return raw.Trim();

        int start;
        char closing;
        if (objStart >= 0 && (arrStart < 0 || objStart < arrStart))
        {
            start = objStart;
            closing = '}';
        }
        else
        {
            start = arrStart;
            closing = ']';
        }

        int end = raw.LastIndexOf(closing);
        return end > start ? raw.Substring(start, end - start + 1) : raw.Trim();
    }

    private static string PrettyPrint(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
        }
        catch
        {
            return json;
        }
    }

    private static string BuildFeedback(ValidationError error, string extracted)
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

        if (!string.IsNullOrWhiteSpace(extracted))
        {
            sb.AppendLine();
            sb.AppendLine("Your output was:");
            sb.AppendLine(extracted);
        }

        return sb.ToString().TrimEnd();
    }
}
