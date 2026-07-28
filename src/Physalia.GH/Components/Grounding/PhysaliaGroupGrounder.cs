// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace Physalia.GH.Components;

/// <summary>
/// Group-scoped variant of <see cref="CanvasStateGrounder"/>: grounds the model with only the
/// contents of the master "Physalia" group — the shared workspace every LLM placement is enrolled
/// into automatically — instead of the whole canvas. Use it when the canvas carries unrelated
/// pre-existing work that would only confuse the model; the user opts components INTO the model's
/// view by moving them into the group. The exported checksum carries a group-scoped prefix, so
/// patches are verified and applied against the exact frame the model saw.
/// </summary>
public class PhysaliaGroupGrounder : CanvasStateGrounder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PhysaliaGroupGrounder"/> class.
    /// </summary>
    public PhysaliaGroupGrounder()
        : base(
            "Physalia Group Components",
            "PhyGrp",
            "Grounds the model with only the contents of the 'Physalia' group — the shared workspace where LLM-placed components land automatically and the user can drop components for the model to read. The rest of the canvas stays out of the model's view. Wire into a Conversation Log's Grounding input instead of Canvas State.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7C3E9A15-2D48-4B7F-9E61-0A8D5C4F2B93");

    /// <inheritdoc/>
    protected override bool GroupScope => true;
}
