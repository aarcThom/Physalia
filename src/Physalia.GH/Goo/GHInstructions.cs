// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for <see cref="Instructions"/>.
/// </summary>
public class GH_Instructions : GH_Goo<Instructions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_Instructions"/> class with no value.
    /// </summary>
    public GH_Instructions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_Instructions"/> class with the given value.
    /// </summary>
    /// <param name="value">The instructions to wrap.</param>
    public GH_Instructions(Instructions value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    public override bool IsValid => Value is not null;

    /// <inheritdoc/>
    public override string TypeName => "Instructions";

    /// <inheritdoc/>
    public override string TypeDescription => "Conversation history and system prompt bundled for inference.";

    /// <inheritdoc/>
    public override IGH_Goo Duplicate() => new GH_Instructions(Value);

    /// <inheritdoc/>
    public override string ToString() => Value is null ? string.Empty : ConversationHelpers.ToDisplayString(Value.Conversation);

    /// <inheritdoc/>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer) => true;

    /// <inheritdoc/>
    public override bool Read(GH_IO.Serialization.GH_IReader reader) => true;
}
