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
            "Shows the model only what is inside the Physalia group — the shared workspace everything it places lands in. The rest of your canvas stays out of sight; move a component into the group to let the model read it. Use this in place of Canvas State when the file also holds unrelated work.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7C3E9A15-2D48-4B7F-9E61-0A8D5C4F2B93");

    /// <inheritdoc/>
    protected override bool GroupScope => true;

    /// <inheritdoc/>
    protected override string GroundingOutputDescription =>
        "The contents of the Physalia group only, as the model will read them, stamped with which version it saw. Wire into a Conversation Log's Grounding input.";
}
