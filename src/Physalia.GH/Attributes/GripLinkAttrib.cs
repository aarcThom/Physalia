// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;

namespace Physalia.GH.Attributes;

/// <summary>
/// Base attributes for components that link to other document objects via the bottom-centre drag
/// grip and render bezier wires to each linked target. The grip, wire rendering, and drag state
/// machine come from <see cref="ArrowAttributeBase"/>; this layer turns a drop into a target
/// link/unlink. Subclasses define what counts as a valid target, how links are stored, the wire
/// colours, and where on the target the wire lands.
/// </summary>
public abstract class GripLinkAttrib : ArrowAttributeBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GripLinkAttrib"/> class.
    /// </summary>
    /// <param name="component">The component that owns these attributes.</param>
    protected GripLinkAttrib(IGH_Component component)
        : base(component)
    {
    }

    /// <summary>
    /// Gets the InstanceGuids of all currently linked targets.
    /// </summary>
    protected abstract IEnumerable<Guid> LinkedTargets { get; }

    /// <summary>
    /// Determines whether a document object is a valid drop target for this link type.
    /// </summary>
    /// <param name="obj">The document object under the drop point.</param>
    /// <returns>true if a drop on this object should connect or disconnect.</returns>
    protected abstract bool IsValidTarget(IGH_DocumentObject obj);

    /// <summary>
    /// Records a link to the target. Called on a normal drop over a valid target.
    /// </summary>
    /// <param name="targetGuid">The InstanceGuid of the drop target.</param>
    protected abstract void OnConnect(Guid targetGuid);

    /// <summary>
    /// Removes a link to the target. Called on a Ctrl+drop over a valid target.
    /// </summary>
    /// <param name="targetGuid">The InstanceGuid of the drop target.</param>
    protected abstract void OnDisconnect(Guid targetGuid);

    /// <summary>
    /// Returns the point on the target's bounds where the wire should land
    /// (e.g. bottom centre for incoming-feedback targets, top centre for script targets).
    /// </summary>
    /// <param name="targetBounds">The target's attribute bounds.</param>
    /// <returns>The wire end point in canvas coordinates.</returns>
    protected abstract PointF GetTargetAnchor(RectangleF targetBounds);

    /// <inheritdoc/>
    public override IEnumerable<PointF> SettledEndpoints(GH_Document doc)
    {
        foreach (Guid guid in LinkedTargets)
        {
            IGH_DocumentObject? target = doc.FindObject(guid, false);
            if (target is not null)
            {
                yield return GetTargetAnchor(target.Attributes.Bounds);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>Connects to a valid target on a normal drop; disconnects on a Ctrl+drop.</remarks>
    public override void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl)
    {
        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (obj.Attributes.Bounds.Contains(dropPoint) && IsValidTarget(obj))
            {
                if (ctrl)
                {
                    OnDisconnect(obj.InstanceGuid);
                }
                else
                {
                    OnConnect(obj.InstanceGuid);
                }

                Owner.ExpireSolution(true);
                break;
            }
        }
    }
}
