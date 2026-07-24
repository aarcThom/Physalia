// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using GH_IO.Serialization;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapping a <see cref="HumanTool"/> — an affordance for the human in the chat
/// window (a geometry-snapshot button, image attachments). Human-tool components output it; the
/// Conversation Log reads the wired tools to decide which affordances the chat window offers.
/// Deliberately NOT a <see cref="GH_Grounding"/>: human tools never touch the system prompt and
/// are never advertised to the model.
/// </summary>
public class GH_HumanTool : PhyGoo<GH_HumanTool, HumanTool>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_HumanTool"/> class with no value.
    /// </summary>
    public GH_HumanTool()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_HumanTool"/> class wrapping the given tool.
    /// </summary>
    /// <param name="tool">The human tool to wrap.</param>
    public GH_HumanTool(HumanTool tool)
        : base(tool)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "Human Tool";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "An affordance for the human in the chat window (geometry snapshot, image attachments) — not sent to the model.";

    /// <inheritdoc/>
    public override string ToString() =>
        Value is null ? "(empty human tool)" : Value.GetType().Name;

    /// <inheritdoc/>
    public override bool CastFrom(object source)
    {
        switch (source)
        {
            case HumanTool tool:
                Value = tool;
                return true;
            case GH_HumanTool goo:
                Value = goo.Value;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: human tools are re-emitted live by their producer components,
    /// so the goo itself stores nothing.</remarks>
    public override bool Write(GH_IWriter writer) => true;

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: human tools are re-emitted live by their producer components,
    /// so the goo itself stores nothing.</remarks>
    public override bool Read(GH_IReader reader) => true;
}
