// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#if WINDOWS
#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.GhJSON;

namespace Physalia.GH.Components;

/// <summary>
/// Interactive component that exports a user-selected set of Grasshopper objects to a
/// <c>.ghjson</c> file. Setting <c>Run</c> to true starts a canvas selection: the user picks
/// the objects to export, presses Enter to confirm (Esc to cancel), then chooses a destination
/// file in the save dialog.
/// </summary>
public class Serializer : PhyBase
{
    private bool _lastRun;
    private bool _interactionActive;
    private GH_Canvas? _hookedCanvas;
    private string? _statusMessage;
    private GH_RuntimeMessageLevel _statusLevel = GH_RuntimeMessageLevel.Remark;

    /// <summary>
    /// Initializes a new instance of the <see cref="Serializer"/> class.
    /// </summary>
    public Serializer()
        : base("Serializer", "Serialize", "Exports a selection of Grasshopper objects to a .ghjson file.", "Serializers")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B2F7A4E1-3C8D-4E5A-9F60-1A2B3C4D5E6F");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter(
            "Run",
            "R",
            "Set to true to begin selecting objects to export. Press Enter to confirm, Esc to cancel.",
            GH_ParamAccess.item,
            false);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No outputs: this component is a pure trigger driven by an interactive selection.
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Emit the result of any completed interaction from the previous (deferred) solve.
        if (_statusMessage is not null)
        {
            AddRuntimeMessage(_statusLevel, _statusMessage);
            _statusMessage = null;
        }

        bool run = false;
        if (!DA.GetData(0, ref run))
        {
            return;
        }

        // Trigger on the rising edge of Run, and never while an interaction is already in flight.
        // The selection prompt is drawn as a persistent canvas overlay (OnCanvasPostPaintWidgets)
        // rather than a runtime message, whose text only shows while the component is hovered.
        if (run && !_lastRun && !_interactionActive)
        {
            BeginSelection();
        }

        _lastRun = run;
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        Unhook();
        base.RemovedFromDocument(document);
    }

    /// <summary>
    /// Begins the canvas selection interaction by hooking the active canvas keyboard.
    /// </summary>
    /// <returns>true if the interaction started; false if there is no active canvas.</returns>
    private bool BeginSelection()
    {
        GH_Canvas? canvas = Instances.ActiveCanvas;
        if (canvas?.Document is null)
        {
            _statusMessage = "No active Grasshopper canvas to export from.";
            _statusLevel = GH_RuntimeMessageLevel.Warning;
            return false;
        }

        _hookedCanvas = canvas;
        _interactionActive = true;

        // Start from a clean selection so the user picks exactly what they want.
        canvas.Document.DeselectAll();
        canvas.Focus();
        canvas.KeyDown += OnCanvasKeyDown;
        canvas.CanvasPostPaintWidgets += OnCanvasPostPaintWidgets;
        canvas.Refresh();
        return true;
    }

    /// <summary>
    /// Handles Enter (confirm) and Esc (cancel) while the selection interaction is active.
    /// </summary>
    /// <param name="sender">The canvas raising the event.</param>
    /// <param name="e">The key event arguments.</param>
    private void OnCanvasKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_interactionActive)
        {
            return;
        }

        if (e.KeyCode is Keys.Enter or Keys.Return)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CompleteSelection();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CancelSelection();
        }
    }

    /// <summary>
    /// Draws a persistent prompt banner across the top of the canvas for the duration of the
    /// selection interaction. Drawn as a screen-space widget so it stays visible regardless of
    /// cursor position (unlike a runtime message, whose text only shows on hover).
    /// </summary>
    /// <param name="sender">The canvas being painted.</param>
    private void OnCanvasPostPaintWidgets(GH_Canvas sender)
    {
        if (!_interactionActive || sender?.Graphics is null)
        {
            return;
        }

        Graphics g = sender.Graphics;

        // CanvasPostPaintWidgets paints under the canvas pan/zoom transform. Reset to device
        // space so the banner is pinned to the top-left of the window regardless of pan/zoom.
        Matrix oldTransform = g.Transform;
        SmoothingMode oldMode = g.SmoothingMode;
        g.ResetTransform();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        const string text = "Serializer — select the objects to export, then press Enter to confirm  (Esc to cancel)";
        Font font = GH_FontServer.Standard;

        SizeF textSize = g.MeasureString(text, font);
        const float margin = 10f;
        float boxWidth = textSize.Width + 24f;
        float boxHeight = textSize.Height + 14f;
        var box = new RectangleF(margin, margin, boxWidth, boxHeight);

        using (var background = new SolidBrush(Color.FromArgb(235, 30, 30, 30)))
        using (var border = new Pen(Color.FromArgb(255, 255, 191, 0), 1.5f))
        using (var foreground = new SolidBrush(Color.White))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.FillRectangle(background, box);
            g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            g.DrawString(text, font, foreground, box, format);
        }

        g.SmoothingMode = oldMode;
        g.Transform = oldTransform;
        oldTransform.Dispose();
    }

    /// <summary>
    /// Confirms the selection, prompts for a destination file, and writes the .ghjson export.
    /// </summary>
    private void CompleteSelection()
    {
        GH_Document? doc = _hookedCanvas?.Document;
        Unhook();

        List<IGH_DocumentObject> selected = doc?.SelectedObjects()?.ToList() ?? new List<IGH_DocumentObject>();
        if (selected.Count == 0)
        {
            SetStatus("No objects were selected — nothing was exported.", GH_RuntimeMessageLevel.Warning);
            return;
        }

        // Capture the GUIDs up front so the export is unaffected by any focus/selection change
        // caused by the modal save dialog.
        List<Guid> guids = selected.Select(o => o.InstanceGuid).ToList();

        string? path = PromptForSavePath();
        if (path is null)
        {
            SetStatus("Export cancelled at the save dialog.", GH_RuntimeMessageLevel.Remark);
            return;
        }

        try
        {
            GhJsonBridge.ExportToFile(guids, path);
            SetStatus($"Exported {selected.Count} object(s) to {path}", GH_RuntimeMessageLevel.Remark);
        }
        catch (Exception ex)
        {
            SetStatus($"Export failed: {ex.Message}", GH_RuntimeMessageLevel.Error);
        }
    }

    /// <summary>
    /// Cancels the selection interaction without exporting.
    /// </summary>
    private void CancelSelection()
    {
        Unhook();
        SetStatus("Export cancelled.", GH_RuntimeMessageLevel.Remark);
    }

    /// <summary>
    /// Shows a save-file dialog for a .ghjson destination.
    /// </summary>
    /// <returns>The chosen file path, or null if the user cancelled.</returns>
    private static string? PromptForSavePath()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export GhJSON",
            Filter = "GhJSON files (*.ghjson)|*.ghjson|All files (*.*)|*.*",
            DefaultExt = "ghjson",
            FileName = "export.ghjson",
            AddExtension = true,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>
    /// Detaches the canvas keyboard hook and clears interaction state.
    /// </summary>
    private void Unhook()
    {
        if (_hookedCanvas is not null)
        {
            _hookedCanvas.KeyDown -= OnCanvasKeyDown;
            _hookedCanvas.CanvasPostPaintWidgets -= OnCanvasPostPaintWidgets;
            _hookedCanvas.Refresh();
            _hookedCanvas = null;
        }

        _interactionActive = false;
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
#endif
