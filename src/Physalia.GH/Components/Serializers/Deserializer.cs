// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Drawing;
using System.IO;
using GhJSON.Core;
using GhJSON.Core.SchemaModels;
using GhJSON.Grasshopper;
using GhJSON.Grasshopper.PutOperations;
using Grasshopper.Kernel;

namespace Physalia.GH.Components;

/// <summary>
/// Reads a <c>.ghjson</c> file and places the components it describes onto the canvas,
/// positioned directly to the right of this component. Setting <c>Run</c> to true imports the
/// file referenced by <c>File Path</c>, provided it is valid GhJSON.
/// </summary>
public class Deserializer : PhyBase
{
    private const float PlacementGap = 50f;

    private bool _lastRun;
    private bool _pending;
    private string _pendingPath = string.Empty;
    private string? _statusMessage;
    private GH_RuntimeMessageLevel _statusLevel = GH_RuntimeMessageLevel.Remark;

    /// <summary>
    /// Initializes a new instance of the <see cref="Deserializer"/> class.
    /// </summary>
    public Deserializer()
        : base("Deserializer", "Deserialize", "Imports a .ghjson file and places its components to the right of this component.", "Serializers")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C3A8D5F2-1E7B-4A9C-8D60-2F4B6E8A0C13");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "File Path",
            "F",
            "Path to a .ghjson file to import.",
            GH_ParamAccess.item);

        pManager.AddBooleanParameter(
            "Run",
            "R",
            "Set to true to place the file's components on the canvas, directly to the right of this component.",
            GH_ParamAccess.item,
            false);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No outputs: this component is a trigger that mutates the canvas as a side effect.
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Emit the result of a completed (deferred) placement from the previous solve.
        if (_statusMessage is not null)
        {
            AddRuntimeMessage(_statusLevel, _statusMessage);
            _statusMessage = null;
        }

        string path = string.Empty;
        bool run = false;
        if (!DA.GetData(0, ref path) || !DA.GetData(1, ref run))
        {
            return;
        }

        // Trigger on the rising edge of Run, never while a placement is already queued.
        if (run && !_lastRun && !_pending)
        {
            BeginPlacement(path);
        }

        _lastRun = run;
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        if (_pending)
        {
            Rhino.RhinoApp.Idle -= OnIdlePlace;
            _pending = false;
        }

        base.RemovedFromDocument(document);
    }

    /// <summary>
    /// Validates the file on the solve thread, then queues the (document-mutating) placement to
    /// run outside the current solution.
    /// </summary>
    /// <param name="path">The .ghjson file path.</param>
    private void BeginPlacement(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"File not found: {path}");
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Could not read file: {ex.Message}");
            return;
        }

        if (!GhJson.IsValid(json, out string? message))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Not a valid GhJSON file. {message}");
            return;
        }

        // Put() adds objects and triggers its own NewSolution, so it must run outside the current
        // solution. RhinoApp.Idle fires on the UI thread once the solution has settled.
        _pendingPath = path;
        _pending = true;
        Rhino.RhinoApp.Idle += OnIdlePlace;
    }

    /// <summary>
    /// One-shot idle handler that performs the queued placement after the solution settles.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnIdlePlace(object? sender, EventArgs e)
    {
        Rhino.RhinoApp.Idle -= OnIdlePlace;
        PlaceDocument(_pendingPath);
        _pending = false;
    }

    /// <summary>
    /// Loads the document and places it on the canvas to the right of this component.
    /// </summary>
    /// <param name="path">The .ghjson file path.</param>
    private void PlaceDocument(string path)
    {
        try
        {
            GhJsonDocument doc = GhJson.FromFile(path);
            if (doc.Components is null || doc.Components.Count == 0)
            {
                SetStatus("The GhJSON file contains no components to place.", GH_RuntimeMessageLevel.Warning);
                return;
            }

            var options = new PutOptions
            {
                Offset = ComputeOffsetRightOfComponent(doc),
                AutoOffset = false,
                CreateConnections = true,
                CreateGroups = true,
                RegenerateInstanceGuids = true,
                SkipInvalidComponents = true,
                SelectPlacedObjects = true,
            };

            PutResult result = GhJsonGrasshopper.Put(doc, options);
            if (result.Success)
            {
                string warnings = result.Warnings.Count > 0 ? $" ({result.Warnings.Count} warning(s))" : string.Empty;
                SetStatus(
                    $"Placed {result.ComponentsPlaced} component(s) and {result.ConnectionsCreated} connection(s){warnings}.",
                    GH_RuntimeMessageLevel.Remark);
            }
            else
            {
                SetStatus($"Placement failed: {result.ErrorMessage}", GH_RuntimeMessageLevel.Error);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Import failed: {ex.Message}", GH_RuntimeMessageLevel.Error);
        }
    }

    /// <summary>
    /// Computes the offset that maps the imported content's top-left pivot to a point just to the
    /// right of this component, so the placed objects appear beside the Deserializer.
    /// </summary>
    /// <param name="doc">The document being placed.</param>
    /// <returns>The pivot offset to apply to every placed object.</returns>
    private PointF ComputeOffsetRightOfComponent(GhJsonDocument doc)
    {
        float srcMinX = float.MaxValue;
        float srcMinY = float.MaxValue;
        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Pivot is not null)
            {
                srcMinX = Math.Min(srcMinX, (float)component.Pivot.X);
                srcMinY = Math.Min(srcMinY, (float)component.Pivot.Y);
            }
        }

        if (srcMinX == float.MaxValue)
        {
            // No pivots in the file — anchor the content to the target directly.
            srcMinX = 0f;
            srcMinY = 0f;
        }

        RectangleF bounds = Attributes.Bounds;
        float targetX = bounds.Right + PlacementGap;
        float targetY = bounds.Y;

        return new PointF(targetX - srcMinX, targetY - srcMinY);
    }

    /// <summary>
    /// Stores a status message and re-solves so it surfaces as a runtime message on the main thread.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="level">The runtime message level.</param>
    private void SetStatus(string message, GH_RuntimeMessageLevel level)
    {
        _statusMessage = message;
        _statusLevel = level;
        ExpireSolution(true);
    }
}
