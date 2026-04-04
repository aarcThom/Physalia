// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;
using Physalia.Core.Prompts;

namespace Physalia.GH.ParamTypes;

/// <summary>
/// Grasshopper Goo wrapper for <see cref="Conversation"/>, enabling a shared
/// <see cref="Conversation"/> instance to be passed between components as a
/// typed parameter on the Grasshopper canvas.
/// </summary>
/// <remarks>
/// The wrapped <see cref="Conversation"/> is shared by reference. <see cref="Duplicate"/>
/// returns a new Goo wrapping the same instance, so that CREST can append assistant
/// responses directly without requiring a back-reference to the Prompt component.
/// Serialisation is a no-op here — the Prompt component is responsible for persisting
/// conversation history via its own <c>Write</c>/<c>Read</c> overrides.
/// </remarks>
public class ConversationGoo : GH_Goo<Conversation>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationGoo"/> class with no conversation set.
    /// </summary>
    public ConversationGoo() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationGoo"/> class.
    /// </summary>
    /// <param name="conversation">The conversation to wrap.</param>
    public ConversationGoo(Conversation conversation) { Value = conversation; }

    /// <summary>
    /// Gets a value indicating whether the wrapped conversation is non-null.
    /// </summary>
    public override bool IsValid => Value != null;

    /// <summary>
    /// Gets the display name of this Goo type.
    /// </summary>
    public override string TypeName => "Conversation";

    /// <summary>
    /// Gets a human-readable description of this Goo type.
    /// </summary>
    public override string TypeDescription => "A multi-turn LLM conversation history";

    /// <summary>
    /// Returns a new Goo wrapping the same <see cref="Conversation"/> instance.
    /// </summary>
    /// <returns>A new <see cref="ConversationGoo"/> sharing the same underlying conversation.</returns>
    public override IGH_Goo Duplicate() => new ConversationGoo(Value);

    /// <summary>
    /// Returns a display string showing the number of messages in the conversation,
    /// or "null" if no conversation is set.
    /// </summary>
    /// <returns>A human-readable summary of the wrapped conversation.</returns>
    public override string ToString() =>
        Value == null ? "null" : $"Conversation ({Value.Messages.Count} messages)";

    /// <summary>
    /// Casting out is not supported.
    /// </summary>
    /// <param name="target">The object to cast to.</param>
    /// <returns>false.</returns>
    public override bool CastTo<Q>(ref Q target) => false;

    /// <summary>
    /// Casting in is not supported.
    /// </summary>
    /// <param name="source">The object to cast from.</param>
    /// <returns>false.</returns>
    public override bool CastFrom(object source) => false;

    /// <summary>
    /// Serialisation stub — intentionally writes nothing; the Prompt component persists conversation history.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer) => true;

    /// <summary>
    /// Deserialisation stub — intentionally reads nothing; the Prompt component restores conversation history.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IO.Serialization.GH_IReader reader) => true;
}
