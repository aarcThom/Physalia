// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.Kernel;

namespace Physalia.GH.Attributes.UiComponents
{
    /// <summary>
    /// A button with text and a triangle to be used as a dropdown menu.
    /// </summary>
    public class DropDownButton
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DropDownButton"/> class.
        /// A button with text and a triangle to be used as a dropdown menu.
        /// </summary>
        /// <param name="bounds">The bounds of the dropdown button.</param>
        /// <param name="owner">The owning GH component.</param>
        /// <param name="graphics">The GH drawing surface.</param>
        /// <param name="buttonText">The text to be displayed on the button.</param>
        public DropDownButton(RectangleF bounds, IGH_Component owner, Graphics graphics, string buttonText)
        {
            var outlineStyle = GetOutlineStyle(owner);
            DrawButton(graphics, bounds, outlineStyle);
            DrawTriangle(graphics, bounds);
            DrawText(graphics, bounds, buttonText);
        }

        private static Pen GetOutlineStyle(IGH_Component ownerIn)
        {
            // declare the pens / brushes / pallets we will need to draw the custom objects
            // - defaults for blank / message levels
            Pen outLine = PhyPalette.BlankOutline;

            // retrieve the proper pens / brushes from our CompColors class
            switch (ownerIn.RuntimeMessageLevel)
            {
                case GH_RuntimeMessageLevel.Warning:
                    // assign warning values
                    outLine = PhyPalette.WarnOutline;
                    break;

                case GH_RuntimeMessageLevel.Error:
                    // assign warning values
                    outLine = PhyPalette.ErrorOutline;
                    break;
            }

            return outLine;
        }

        private static void DrawButton(Graphics grphx, RectangleF btnBnds, Pen outlinePen)
        {
            grphx.FillRectangle(PhyPalette.SmallButton(btnBnds.Top, btnBnds.Bottom), btnBnds);
            var border = outlinePen;
            grphx.DrawRectangle(border, btnBnds);
        }

        private static void DrawTriangle(Graphics grphx, RectangleF btnBnds)
        {
            float cx = btnBnds.Left + 8f;
            float cy = btnBnds.Top + (btnBnds.Height / 2f);
            var tri = new PointF[]
            {
            new (cx - 4f, cy - 4f),
            new (cx + 4f, cy - 4f),
            new (cx,      cy + 4f),
            };
            grphx.FillPolygon(Brushes.White, tri);
        }

        private static void DrawText(Graphics grphx, RectangleF btnbnds, string txt)
        {
            var labelRect = new RectangleF(btnbnds.Left + 12f, btnbnds.Top + 4f, btnbnds.Width - 12f, btnbnds.Height - 8f);

            var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            grphx.DrawString(txt, GH_FontServer.StandardAdjusted, Brushes.White, labelRect, fmt);
        }
    }
}
