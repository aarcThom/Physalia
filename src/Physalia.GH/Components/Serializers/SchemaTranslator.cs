// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Validation;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// Converts a PhySchema JSON string to GhJSON format by injecting canvas pivot
/// positions computed from a hierarchical layout pass.
/// </summary>
public class SchemaTranslator : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaTranslator"/> class.
    /// </summary>
    public SchemaTranslator()
        : base(
            "Schema Translator",
            "SchT",
            "Converts a PhySchema JSON string to GhJSON format by computing canvas pivot positions.",
            "Serializers")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("DDDFAF65-212D-45B9-B581-C0EC806C4106");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Data In", "D",
            "PhySchema JSON string to translate.",
            GH_ParamAccess.item,
            string.Empty);

        pManager.AddTextParameter(
            "Schema In", "S",
            "JSON schema string used to validate Data In. Pass-through (no validation) if empty.",
            GH_ParamAccess.item,
            string.Empty);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Data Out", "D",
            "GhJSON-formatted string on success. Empty on failure.",
            GH_ParamAccess.item);

        pManager.AddBooleanParameter(
            "Success Trigger", "ST",
            "Fires true when translation succeeds.",
            GH_ParamAccess.item);

        pManager.AddBooleanParameter(
            "Fail Trigger", "FT",
            "Fires true when translation fails.",
            GH_ParamAccess.item);

        pManager.AddTextParameter(
            "Feedback", "F",
            "Error message on failure. Empty on success.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string data   = string.Empty;
        string schema = string.Empty;

        DA.GetData(0, ref data);
        DA.GetData(1, ref schema);

        if (string.IsNullOrWhiteSpace(data))
        {
            DA.SetData(0, string.Empty);
            DA.SetData(1, false);
            DA.SetData(2, false);
            DA.SetData(3, string.Empty);
            return;
        }

        if (!string.IsNullOrWhiteSpace(schema))
        {
            var validationResult = SchemaValidator.Validate(data, schema);
            if (validationResult is Result<string, ValidationError>.Err validationErr)
            {
                DA.SetData(0, string.Empty);
                DA.SetData(1, false);
                DA.SetData(2, true);
                DA.SetData(3, validationErr.Error.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, validationErr.Error.Message);
                return;
            }
        }

        PhySchemaDocument? doc = PhySchemaDocument.FromJson(data);
        if (doc is null)
        {
            const string msg = "Failed to deserialise the input as a PhySchema document.";
            DA.SetData(0, string.Empty);
            DA.SetData(1, false);
            DA.SetData(2, true);
            DA.SetData(3, msg);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, msg);
            return;
        }

        string ghJson = TranslateToGhJson(doc);
        DA.SetData(0, ghJson);
        DA.SetData(1, true);
        DA.SetData(2, false);
        DA.SetData(3, string.Empty);
    }

    private static string TranslateToGhJson(PhySchemaDocument schema)
    {
        IReadOnlyDictionary<int, PointF> positions = HierarchicalLayout.ComputePositions(schema);

        var root = new JsonObject
        {
            ["schema"] = "1.0",
        };

        var componentsArray = new JsonArray();
        foreach (PhySchemaComponent component in schema.Components ?? Array.Empty<PhySchemaComponent>())
        {
            var compNode = new JsonObject
            {
                ["name"]         = component.Name,
                ["instanceGuid"] = component.InstanceGuid,
                ["id"]           = component.Id,
            };

            if (positions.TryGetValue(component.Id, out PointF pivot))
                compNode["pivot"] = $"{(int)pivot.X},{(int)pivot.Y}";
            else
                compNode["pivot"] = "50,50";

            if (component.ComponentState.HasValue)
                compNode["componentState"] = JsonNode.Parse(component.ComponentState.Value.GetRawText());

            componentsArray.Add(compNode);
        }
        root["components"] = componentsArray;

        var connectionsArray = new JsonArray();
        foreach (PhySchemaConnection connection in schema.Connections ?? Array.Empty<PhySchemaConnection>())
        {
            connectionsArray.Add(new JsonObject
            {
                ["from"] = new JsonObject
                {
                    ["id"]        = connection.From.Id,
                    ["paramName"] = connection.From.ParamName,
                },
                ["to"] = new JsonObject
                {
                    ["id"]        = connection.To.Id,
                    ["paramName"] = connection.To.ParamName,
                },
            });
        }
        root["connections"] = connectionsArray;

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
