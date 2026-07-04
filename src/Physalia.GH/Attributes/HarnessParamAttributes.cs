// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using HarnessGroup = Physalia.GH.Harness.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Linked-parameter attributes that drop their grips while the owning harness is collapsed.
/// Identical to <see cref="GH_LinkedParamAttributes"/> otherwise. The canvas finds wire-drag
/// start points via a parameter attribute's
/// <see cref="GH_Attributes{T}.HasInputGrip"/>/<see cref="GH_Attributes{T}.HasOutputGrip"/>
/// (see <c>GH_Document.RelevantObjectAtPoint</c>), so reporting no grips makes a collapsed
/// harness — both its proxy Chat and every hidden member piled at the proxy point —
/// non-wireable, while leaving the nodes draggable and their existing connections intact (wire
/// rendering reads the grip <em>positions</em>, not these flags).
/// </summary>
public class HarnessParamAttributes : GH_LinkedParamAttributes
{
    private readonly HarnessGroup _harness;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessParamAttributes"/> class.
    /// </summary>
    /// <param name="param">The parameter these attributes belong to.</param>
    /// <param name="parent">The owning component's attributes.</param>
    /// <param name="harness">The harness whose collapse state gates the grips.</param>
    public HarnessParamAttributes(IGH_Param param, IGH_Attributes parent, HarnessGroup harness)
        : base(param, parent)
    {
        _harness = harness;
    }

    /// <inheritdoc/>
    public override bool HasInputGrip => !_harness.Collapsed && base.HasInputGrip;

    /// <inheritdoc/>
    public override bool HasOutputGrip => !_harness.Collapsed && base.HasOutputGrip;
}
