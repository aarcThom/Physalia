// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel;
using System.Drawing;
using System.Reflection;

namespace Physalia.GH.Components;

public abstract class PhyBase : GH_Component
{
    //grabbing embedded resources
    protected readonly Assembly GHAssembly = Assembly.GetExecutingAssembly();

    protected string? IconPath;
    private Bitmap? _iconCache;

    protected PhyBase(string name, string nickname, string description, string subCategory)
        : base(name, nickname, description, "Physalia", subCategory)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether this component is currently hidden as a
    /// collapsed member of a harness. When true its attributes shrink the node and its wires
    /// to a point at <see cref="HarnessCollapsePoint"/> and draw nothing, while the component
    /// stays wired and keeps solving. Managed by <see cref="Harness.Harness"/>; session-only
    /// — the owning Chat persists the group and collapse state, not the member.
    /// </summary>
    public bool HarnessCollapsed { get; set; }

    /// <summary>
    /// Gets or sets the shared point all collapsed members shrink to — the proxy Chat's
    /// pivot. Every member of one harness uses the same point so the wires between them become
    /// zero-length and vanish.
    /// </summary>
    public PointF HarnessCollapsePoint { get; set; }

    /// <summary>
    /// Provides the default Physalia attributes, which honour the harness collapse state.
    /// Components with bespoke drawing override this and call the collapse guard themselves.
    /// </summary>
    public override void CreateAttributes()
    {
        m_attributes = new Attributes.PhyComponentAttributes(this);
    }

    /// <summary>
    /// Provides an Icon for the component. Resolves the embedded resource named
    /// after the concrete component type (e.g. <c>SchemaValidator</c> → <c>SchemaValidator.png</c>),
    /// honouring an explicit <see cref="IconPath"/> override if one is set, and
    /// falling back to the generic brain icon when no matching resource exists.
    /// </summary>
    protected override Bitmap Icon
    {
        get
        {
            if (_iconCache != null)
            {
                return _iconCache;
            }

            // Explicit override wins; otherwise derive the resource name from the runtime type.
            string resourceName = IconPath ?? $"Physalia.GH.Resources.{GetType().Name}.png";

            using System.IO.Stream? stream =
                GHAssembly.GetManifestResourceStream(resourceName)
                ?? GHAssembly.GetManifestResourceStream("Physalia.GH.Resources.brain.png");

            // Fallback to an empty bitmap so GH doesn't crash if no resource is found.
            _iconCache = stream != null ? new Bitmap(stream) : new Bitmap(24, 24);
            return _iconCache;
        }
    }
}
