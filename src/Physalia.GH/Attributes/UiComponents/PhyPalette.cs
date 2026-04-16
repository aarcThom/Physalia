using System.Drawing;
using System.Drawing.Drawing2D;

namespace Physalia.GH.Attributes.UiComponents;

public static class PhyPalette
{   //fields
    private static readonly Color BlankOutlineCol = Color.FromArgb(255, 50, 50, 50);
    private static readonly Color WarnOutlineCol = Color.FromArgb(255, 80, 10, 0);
    private static readonly Color ErrorOutlineCol = Color.FromArgb(255, 60, 0, 0);

    //properties
    public static readonly Pen BlankOutline = new Pen(BlankOutlineCol) { EndCap = System.Drawing.Drawing2D.LineCap.Round };
    public static readonly Pen WarnOutline = new Pen(WarnOutlineCol) { EndCap = System.Drawing.Drawing2D.LineCap.Round };
    public static readonly Pen ErrorOutline = new Pen(ErrorOutlineCol) { EndCap = System.Drawing.Drawing2D.LineCap.Round };

    // methods
    public static Brush SmallButtonBrush(float topY, float botY)
    {
        return new LinearGradientBrush(new PointF(0, topY), new PointF(0, botY), Color.DarkGray, Color.Black);
    }
}
