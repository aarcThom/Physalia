// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Rhino.Geometry;

namespace Physalia.GH.Generation;

/// <summary>
/// Captures the active Rhino viewport as a bounded PNG — the shared camera behind the Geometry
/// Observation guardrail and the Geometry Snapshot grounding. Zooms onto the given bounds (when
/// valid), redraws, captures the frame, and downscales so the longest side stays within the inline
/// image cap. Mutates viewport state, so callers must invoke it on the UI thread outside a
/// Grasshopper solution (Geometry Observation defers to <c>RhinoApp.Idle</c>; the chat window's
/// geometry button already runs on the UI thread between solves).
/// </summary>
internal static class ViewportSnapshot
{
    /// <summary>Longest-side pixel cap for the encoded snapshot, keeping the inline image bounded.</summary>
    private const int MaxImageSide = 1568;

    /// <summary>
    /// Zooms the active viewport onto <paramref name="bounds"/> (when valid) and captures it as PNG
    /// bytes.
    /// </summary>
    /// <param name="bounds">The bounds to frame; an invalid box captures the view as-is.</param>
    /// <param name="imageBytes">The PNG-encoded snapshot on success, else null.</param>
    /// <param name="error">The failure description on failure, else null.</param>
    /// <returns>True when a snapshot was captured.</returns>
    internal static bool TryCapture(BoundingBox bounds, out byte[]? imageBytes, out string? error)
    {
        imageBytes = null;
        error = null;

        try
        {
            Rhino.Display.RhinoView? view = Rhino.RhinoDoc.ActiveDoc?.Views?.ActiveView;
            if (view is null)
            {
                error = "No active Rhino viewport to capture.";
                return false;
            }

            if (bounds.IsValid)
            {
                bounds.Inflate(bounds.Diagonal.Length * 0.05);
                view.ActiveViewport.ZoomBoundingBox(bounds);
                view.Redraw();
            }

            using Bitmap? frame = view.CaptureToBitmap();
            if (frame is null)
            {
                error = "Viewport capture returned no image.";
                return false;
            }

            imageBytes = EncodePng(frame);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Snapshot failed: {ex.Message}";
            return false;
        }
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
}
