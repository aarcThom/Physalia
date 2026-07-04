// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// On receiving a signal, zooms the active Rhino viewport onto the geometry produced upstream
/// (the components a Component Transmitter placed, identified by the GUIDs in its Success payload;
/// or, when the payload carries no GUIDs — e.g. from a Py Transmitter — the whole document's
/// preview geometry) and captures a viewport snapshot. It then mints a single signal carrying the
/// user's <c>Message</c> as its payload plus the snapshot image as an inline content block, and
/// latches that <em>same</em> signal on both the Success and Fail outputs so either downstream
/// branch (forward to a Conversation Log, or back to the LLM Call as feedback) receives identical content.
/// </summary>
public class GeometryObservation : RoutingComponentBase<string>
{
    /// <summary>Index of the subclass-owned Message input (registered before the base Signal input).</summary>
    private const int MessageInputIndex = 0;

    /// <summary>Longest-side pixel cap for the encoded snapshot, keeping the inline image bounded.</summary>
    private const int MaxImageSide = 1568;

    private string _pendingScope = string.Empty;
    private byte[]? _imageBytes;
    private string? _captureError;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryObservation"/> class.
    /// </summary>
    public GeometryObservation()
        : base(
            "Geometry Observation",
            "Geometry Observation",
            "Zooms the Rhino viewport onto the upstream geometry and captures a snapshot. Emits the Message plus the snapshot image on both outputs.",
            "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4A969C47-C1E5-446D-BB20-9F973D5E2E3D");

    /// <inheritdoc/>
    /// <remarks>
    /// The viewport zoom and capture mutate UI state and must run after the solution settles, so
    /// the capture is deferred to <c>RhinoApp.Idle</c> and the read pass is scheduled by this
    /// component rather than auto-scheduled by the base.
    /// </remarks>
    protected override bool AutoScheduleRead => false;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Message",
            "M",
            "Text to accompany the snapshot. Becomes the emitted signal's payload and a text block alongside the image.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[MessageInputIndex].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The incoming payload (the placed components' GUIDs from a Component Transmitter) scopes the
    /// zoom. A run is always worth performing — an empty payload simply falls back to a
    /// whole-document snapshot — so this always returns true.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload ?? string.Empty;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Captures the scope and queues the (UI-mutating) zoom + capture to run outside the current
    /// solution on <c>RhinoApp.Idle</c>. The idle handler requests the read pass when it finishes.
    /// </remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        _pendingScope = data;
        _imageBytes = null;
        _captureError = null;

        // ZoomBoundingBox and CaptureToBitmap touch the viewport, so they cannot run inside the
        // solution. RhinoApp.Idle fires on the UI thread once the solution has settled.
        Rhino.RhinoApp.Idle += OnIdleCapture;
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        if (_captureError != null)
        {
            return RoutingResult.Fail(_captureError, _captureError, GH_RuntimeMessageLevel.Error);
        }

        if (_imageBytes is not { Length: > 0 })
        {
            return RoutingResult.Fail("No snapshot image was produced.", "Snapshot produced no image.", GH_RuntimeMessageLevel.Error);
        }

        string message = string.Empty;
        da.GetData(MessageInputIndex, ref message);
        message ??= string.Empty;

        var blocks = new List<MessageContent>();
        if (StringHelpers.IsNonBlank(message))
        {
            blocks.Add(new TextContent(message));
        }

        blocks.Add(new ImageContent(new InlineImage(_imageBytes, "image/png")));

        PhySignal signal = PhySignal.Mint(SignalOutcome.Success, message, InstanceGuid, Name, blocks);
        return RoutingResult.Broadcast(signal, "Snapshot captured.", GH_RuntimeMessageLevel.Remark);
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        Rhino.RhinoApp.Idle -= OnIdleCapture;
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _pendingScope = string.Empty;
        _imageBytes = null;
        _captureError = null;
    }

    /// <summary>
    /// One-shot idle handler: zooms the active viewport to the scoped geometry, redraws, and
    /// captures the frame, then hands control back to the routing base.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnIdleCapture(object? sender, EventArgs e)
    {
        Rhino.RhinoApp.Idle -= OnIdleCapture;

        try
        {
            Rhino.Display.RhinoView? view = Rhino.RhinoDoc.ActiveDoc?.Views?.ActiveView;
            if (view is null)
            {
                _captureError = "No active Rhino viewport to capture.";
                RequestReadPass();
                return;
            }

            BoundingBox bounds = ComputeScopeBounds(_pendingScope);
            if (bounds.IsValid)
            {
                bounds.Inflate(bounds.Diagonal.Length * 0.05);
                view.ActiveViewport.ZoomBoundingBox(bounds);
                view.Redraw();
            }

            using Bitmap? frame = view.CaptureToBitmap();
            if (frame is null)
            {
                _captureError = "Viewport capture returned no image.";
            }
            else
            {
                _imageBytes = EncodePng(frame);
            }
        }
        catch (Exception ex)
        {
            _captureError = $"Snapshot failed: {ex.Message}";
        }

        RequestReadPass();
    }

    /// <summary>
    /// Unions the preview clipping boxes of the scoped objects: the components named by the
    /// incoming GUID payload, or every preview-capable object on the document (except this one)
    /// when the payload carries no parseable GUIDs.
    /// </summary>
    /// <param name="scope">The incoming signal payload (newline-separated GUIDs, or anything else).</param>
    /// <returns>The combined bounding box, or an invalid box when nothing previewable was found.</returns>
    private BoundingBox ComputeScopeBounds(string scope)
    {
        GH_Document? doc = OnPingDocument();
        if (doc is null)
        {
            return BoundingBox.Empty;
        }

        var scoped = ParseGuids(scope)
            .Select(g => doc.FindObject(g, false))
            .Where(o => o is not null)
            .Select(o => o!)
            .ToList();

        IEnumerable<IGH_DocumentObject> objects = scoped.Count > 0
            ? scoped
            : doc.Objects.Where(o => o.InstanceGuid != InstanceGuid);

        BoundingBox union = BoundingBox.Empty;
        foreach (IGH_DocumentObject obj in objects)
        {
            if (obj is IGH_PreviewObject preview && !preview.Hidden && preview.IsPreviewCapable)
            {
                BoundingBox box = preview.ClippingBox;
                if (box.IsValid)
                {
                    union = BoundingBox.Union(union, box);
                }
            }
        }

        return union;
    }

    /// <summary>
    /// Encodes a captured frame as PNG bytes, downscaling so its longest side does not exceed
    /// <see cref="MaxImageSide"/> to keep the inline image payload bounded.
    /// </summary>
    /// <param name="frame">The captured viewport bitmap.</param>
    /// <returns>The PNG-encoded bytes.</returns>
    private static byte[] EncodePng(Bitmap frame)
    {
        Bitmap toEncode = frame;
        bool dispose = false;

        int longest = Math.Max(frame.Width, frame.Height);
        if (longest > MaxImageSide)
        {
            double scale = (double)MaxImageSide / longest;
            int width = Math.Max(1, (int)Math.Round(frame.Width * scale));
            int height = Math.Max(1, (int)Math.Round(frame.Height * scale));
            toEncode = new Bitmap(frame, new Size(width, height));
            dispose = true;
        }

        try
        {
            using var ms = new MemoryStream();
            toEncode.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        finally
        {
            if (dispose)
            {
                toEncode.Dispose();
            }
        }
    }

    /// <summary>
    /// Parses newline-separated GUIDs from the payload, skipping any line that is not a GUID.
    /// </summary>
    /// <param name="payload">The incoming signal payload.</param>
    /// <returns>The GUIDs found in the payload.</returns>
    private static IEnumerable<Guid> ParseGuids(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        foreach (string line in payload.Split('\n'))
        {
            if (Guid.TryParse(line.Trim(), out Guid guid))
            {
                yield return guid;
            }
        }
    }
}
