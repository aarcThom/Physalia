using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Physalia.GH.Attributes;

public class BrainAttrib : GH_ComponentAttributes
{
    private readonly Brain _brain; // the brain component

    private bool _isDragging; // is the user dragging the wire?
    private PointF _dragPoint; // the current positon of the drag

    private RectangleF _componentGrabbableBounds; // the lower area of the component that can be grabbed as well. Needed so users don't have to be superexact
    private RectangleF _gripBounds; // the actual bounds of the grip
                                    // NOTE: THE ABOVE DOUBLE GRABBABLE AREA CAN PROBABLY BE SIMPLIFIED.

    /*BIG NOTE:
     * I think I need to claim more space for the component but not expand the component rectangle height...
     */



    public BrainAttrib(Brain brain) : base(brain)
    {
        _brain = brain;
        
    }


    protected override void Layout()
    {
        base.Layout();
        _componentGrabbableBounds = GetGripBounds();
    }

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {


        /* Render is called multiple times per canvas redraw — once for each rendering channel
        * (e.g.Wires, Objects, Overlay, etc.)
        * Only draw the custom bezier when GH wires are being drawn
        */

    float gripCtrX = Bounds.Left + Bounds.Width / 2f;
        float gripCtrY = Bounds.Y + Bounds.Height;
        if (channel == GH_CanvasChannel.Objects)
        {
            var gripRadius = 4f;
            _gripBounds = new RectangleF(gripCtrX - gripRadius, gripCtrY - 2f, gripRadius * 2, gripRadius * 2);
            using var fill = new SolidBrush(Color.White);
            using var border = new Pen(Color.Black, 2f);
            graphics.FillEllipse(fill, _gripBounds);
            graphics.DrawEllipse(border, _gripBounds);
        }

        
        if (channel == GH_CanvasChannel.Wires)
        {
            if (_brain.BodyComponent == null && !_isDragging)
            {
                base.Render(canvas, graphics, channel);
                return;
            }
       

            PointF wireEnd;
            if (_isDragging)
            {
                wireEnd = _dragPoint;
            }
            else
            {
                var bodyBounds = _brain.BodyComponent.Attributes.Bounds;
                wireEnd = new PointF(bodyBounds.Left, bodyBounds.Y + bodyBounds.Height / 2f);
            }
             
            var brainBottomPt = new PointF(gripCtrX, gripCtrY);

            float brainBodyMidPt = (wireEnd.X - brainBottomPt.X) * 0.5f;

            // create the color gradient
            var phyBlue = Color.Blue;
            var phyPurple = Color.Purple;
            using var gradient = new LinearGradientBrush(brainBottomPt, wireEnd, phyBlue, phyPurple);
            using var pen = new Pen(gradient, 2f);

            // draw a bezier curver between the BRAIN and BODY
            var bezPt1 = new PointF(brainBottomPt.X, brainBottomPt.Y + 80f);
            var bezPt2 = new PointF(wireEnd.X - brainBodyMidPt, wireEnd.Y);
            graphics.DrawBezier(pen, brainBottomPt, bezPt1, bezPt2, wireEnd);

            //draw the triangle at the tip
            float triWidth = 8f;
            float triHeight = triWidth / 2;

            var tip = new PointF(wireEnd.X + triWidth, wireEnd.Y);
            var baseTop = new PointF(tip.X - triWidth, tip.Y - triHeight / 2);
            var baseBot = new PointF(tip.X - triWidth, tip.Y + triHeight / 2);
            using var triFill = new SolidBrush(phyPurple);
            graphics.FillPolygon(triFill, new[] { tip, baseTop, baseBot });
            
        }

        base.Render(canvas, graphics, channel);
    }


    private RectangleF GetGripBounds()
    {
        float boundsRadius = 10f;
        float boundsCtrX = Bounds.Left + Bounds.Width / 2f;
        float boundsCtrY = Bounds.Y + Bounds.Height;
        return new RectangleF(boundsCtrX - boundsRadius, boundsCtrY - boundsRadius, boundsRadius * 2f, boundsRadius * 2f);

    }

    // EVENT HANDLERS =======================================================================================

    // start drag if grip is hit
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {

        if (_componentGrabbableBounds.Contains(e.CanvasLocation) || _gripBounds.Contains(e.CanvasLocation))
        {
            _isDragging = true;
            _dragPoint = e.CanvasLocation;
            sender.ScheduleRegen(2);

            return GH_ObjectResponse.Capture; // object is active
        }
        return base.RespondToMouseDown(sender, e);
    }

    // update wire end location if being dragged
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_isDragging)
        {
            _dragPoint = e.CanvasLocation;
            sender.ScheduleRegen(2);

            return GH_ObjectResponse.Handled;
        }
        return base.RespondToMouseMove(sender, e);
    }

    // if released while dragging
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_isDragging)
        {
            _isDragging = false;

            // cycle through the components on the canvas
            foreach(var obj in sender.Document.Objects)
            {
                if (obj is Body body && body.Attributes.Bounds.Contains(e.CanvasLocation))
                {
                    _brain.BodyComponent = body; // set the ref'd body
                    break;
                }
            }
            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }
        return base.RespondToMouseUp(sender, e);
    }




}
