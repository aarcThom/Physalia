using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Physalia.GH.Attributes;

public class BodyAttrib : GH_ComponentAttributes
{
    /// <summary>
    /// Custom attributes class for the Physalia component that intercepts
    /// double-click events on the Grasshopper canvas. GH_Component does not
    /// expose a virtual OnDoubleClick method, so the standard approach is to
    /// subclass GH_ComponentAttributes and override RespondToMouseDoubleClick.
    /// The component registers this class via CreateAttributes(), and when
    /// the user double-clicks, we delegate to PhysaliaComponent.OpenScriptEditor()
    /// to launch the Eto.Forms script editor dialog.
    /// </summary>
    public BodyAttrib(GH_Component owner) : base(owner) { }

    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (Owner is Components.Body component)
        {
            component.OpenScriptEditor();
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDoubleClick(sender, e);
    }
}