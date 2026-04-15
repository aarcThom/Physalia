using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using Grasshopper.GUI.Canvas;

namespace Physalia.GH.Attributes
{
    public class PanelBaseAttrib : GH_ComponentAttributes
    {
        // CONSTANTS =======================================================================================
        private const float TitleHeight = 18f;
        private const float GripSize = 8f;
        private const float CornerRadius = 4f;
        private const float MinWidth = 140f;
        private const float MinSectionHeight = 40f;
        private const float DefaultWidth = 220f;
        private const float DefaultConvoHeight = 120f;
        private const float DefaultInputHeight = 80f;
        private const float ConvoPadding = 6f; // room between conversation and convo panel
        private const float ScrollbarWidth = 10f;

        // FIELDS ============================================================================================

        // sizing state — persisted across saves
        private float _width;
        private float _convoHeight; // history section
        private float _inputHeight; // entry section

        private System.Drawing.RectangleF _renderBounds; // the bounds that are rendered.

        public PanelBaseAttrib(IGH_Component component) : base(component)
        {
        }

        protected override void Layout()
        {
            if (_width < MinWidth)
            {
                _width = DefaultWidth;
                _convoHeight = DefaultConvoHeight;
                _inputHeight = DefaultInputHeight;
            }

            float x = Pivot.X;
            float y = Pivot.Y;

            _renderBounds = new System.Drawing.RectangleF(x, y, _width, TitleHeight + _convoHeight + _inputHeight);

            Bounds = _renderBounds;
        }

        protected override void Render(GH_Canvas canvas, System.Drawing.Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

        }
    }
}
