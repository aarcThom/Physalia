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
    /// Poses the active viewport as a camera standing at a point and looking in a direction, captures
    /// that frame as PNG bytes, and puts the viewport back exactly as it was. This is the Take
    /// Snapshot tool's camera: unlike <see cref="TryCapture"/>, which frames existing geometry, this
    /// one answers "what would somebody standing HERE, facing THAT way, see".
    ///
    /// <para>The user's own view is borrowed, not spent. Rhino's own
    /// <c>PushViewProjection</c>/<c>PopViewProjection</c> pair saves and restores the whole
    /// projection, so the restore is exact and happens in a finally — a throw mid-capture must not
    /// leave the model's camera sitting in the user's viewport. Same threading rule as
    /// <see cref="TryCapture"/>: UI thread, outside a Grasshopper solution.</para>
    /// </summary>
    /// <param name="location">Where the camera stands.</param>
    /// <param name="direction">Which way it looks; need not be unitized. Zero-length fails.</param>
    /// <param name="lensLength">35mm-equivalent lens length in millimetres.</param>
    /// <param name="imageBytes">The PNG-encoded frame on success, else null.</param>
    /// <param name="error">The failure description on failure, else null.</param>
    /// <returns>True when a frame was captured.</returns>
    internal static bool TryCaptureFromCamera(
        Point3d location,
        Vector3d direction,
        double lensLength,
        out byte[]? imageBytes,
        out string? error)
    {
        imageBytes = null;
        error = null;

        if (!direction.Unitize())
        {
            error = "The view direction has no length, so there is nothing to look at.";
            return false;
        }

        Rhino.Display.RhinoView? view = Rhino.RhinoDoc.ActiveDoc?.Views?.ActiveView;
        if (view is null)
        {
            error = "No active Rhino viewport to capture.";
            return false;
        }

        Rhino.Display.RhinoViewport viewport = view.ActiveViewport;
        if (viewport.LockedProjection)
        {
            error = "The active Rhino viewport has a locked projection, so the camera cannot be posed. "
                + "Unlock or switch the viewport and try again.";
            return false;
        }

        viewport.PushViewProjection();
        try
        {
            // Perspective first: a lens length is meaningless in a parallel projection, and the
            // active view may well be a Top or Front one.
            if (!viewport.ChangeToPerspectiveProjection(false, lensLength))
            {
                error = "The active Rhino viewport refused a perspective projection, so no snapshot was taken.";
                return false;
            }

            // Target a point one step down the sight line: SetCameraLocations derives the direction
            // from the two points, which is the pair Rhino keeps consistent.
            double reach = TargetReach(location);
            viewport.SetCameraLocations(location + (direction * reach), location);

            // Re-asserted after the camera move, which can rewrite the frustum and with it the
            // effective lens length.
            viewport.Camera35mmLensLength = lensLength;

            // Keep the horizon level, the way a person holds their head — but only when there IS a
            // horizon: looking straight up or down makes world Z a degenerate up vector, and Rhino
            // keeps the up it derived itself.
            if (Math.Abs(direction.Z) < 0.99)
            {
                viewport.CameraUp = Vector3d.ZAxis;
            }

            view.Redraw();

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
        finally
        {
            // Exact restore, whatever happened above.
            viewport.PopViewProjection();
            view.Redraw();
        }
    }

    // How far down the sight line to put the camera target. It only has to be far enough that the
    // location/target pair defines a stable direction and the frustum encloses the model, so it is
    // scaled off the document contents rather than being a bare magic number in model units (which
    // would be metres in one file and millimetres in the next).
    private static double TargetReach(Point3d location)
    {
        BoundingBox scene = Rhino.RhinoDoc.ActiveDoc?.Objects?.BoundingBox ?? BoundingBox.Unset;
        if (!scene.IsValid)
        {
            return 1.0;
        }

        scene.Union(location);
        double diagonal = scene.Diagonal.Length;
        return diagonal > Rhino.RhinoMath.ZeroTolerance ? diagonal : 1.0;
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
