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
    /// Provides the default Physalia attributes. Components with bespoke drawing override this.
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
