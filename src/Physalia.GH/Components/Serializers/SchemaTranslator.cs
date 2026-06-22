// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.Core.Validation;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// Converts a PhySchema JSON string to GhJSON format by injecting canvas pivot
/// positions computed from a hierarchical layout pass.
/// </summary>
public class SchemaTranslator : RoutingComponentBase<string>
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
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Schema In", "Sc",
            "JSON schema string used to validate the incoming PhySchema. Pass-through (no validation) if empty.",
            GH_ParamAccess.item,
            string.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>The PhySchema JSON to translate arrives as the consumed signal's payload.</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — no settle pass needed; all work is in ReadSolve.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: translation has no side effects to push before reading.
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        string schema = string.Empty;
        da.GetData(0, ref schema);

        if (!string.IsNullOrWhiteSpace(schema))
        {
            var validationResult = SchemaValidator.Validate(data, schema);
            if (validationResult is Result<string, ValidationError>.Err validationErr)
            {
                return RoutingResult.Fail(
                    validationErr.Error.Message, validationErr.Error.Message, GH_RuntimeMessageLevel.Warning);
            }
        }

        PhySchemaDocument? doc = PhySchemaDocument.FromJson(data);
        if (doc is null)
        {
            const string msg = "Failed to deserialise the input as a PhySchema document.";
            return RoutingResult.Fail(msg, msg, GH_RuntimeMessageLevel.Error);
        }

        return RoutingResult.Ok(TranslateToGhJson(doc));
    }

    private static string TranslateToGhJson(PhySchemaDocument schema)
    {
        IReadOnlyDictionary<int, PointF> positions = HierarchicalLayout.ComputePositions(schema);
        return GhJsonBridge.SerializePhySchema(schema, positions);
    }
}
