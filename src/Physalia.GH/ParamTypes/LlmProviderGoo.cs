// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;
using Physalia.Core.Providers;

namespace Physalia.GH.ParamTypes;

/// <summary>
/// Grasshopper Goo wrapper for <see cref="LlmProvider"/>, enabling an
/// <see cref="LlmProvider"/> instance to be passed between components as a
/// typed parameter on the Grasshopper canvas.
/// </summary>
/// <remarks>
/// Serialisation is intentionally left as a no-op: the wrapped provider holds
/// a live API key that must not be written to the <c>.gh</c> file. Components
/// that consume this Goo must source it from a DREAM component at runtime.
/// </remarks>
public class LlmProviderGoo : GH_Goo<LlmProvider>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlmProviderGoo"/> class with no provider set.
    /// </summary>
    public LlmProviderGoo() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmProviderGoo"/> class.
    /// </summary>
    /// <param name="provider">The LLM provider to wrap.</param>
    public LlmProviderGoo(LlmProvider provider) { Value = provider; }

    /// <summary>
    /// Gets a value indicating whether the wrapped provider is non-null.
    /// </summary>
    public override bool IsValid => Value != null;

    /// <summary>
    /// Gets the display name of this Goo type.
    /// </summary>
    public override string TypeName => "LlmProvider";

    /// <summary>
    /// Gets a human-readable description of this Goo type.
    /// </summary>
    public override string TypeDescription => "LLM provider and selected model";

    /// <summary>
    /// Creates a shallow copy of this Goo, sharing the same LlmProvider instance.
    /// </summary>
    /// <returns>A new GH_LlmProvider wrapping the same Value.</returns>
    public override IGH_Goo Duplicate() => new LlmProviderGoo(Value);

    /// <summary>
    /// Returns a display string in the form "ProviderName / CurrentModel",
    /// or "null" if no provider is set.
    /// </summary>
    /// <returns>A human-readable summary of the wrapped provider.</returns>
    public override string ToString() => Value == null ? "null" : $"{Value.ProviderName} / {Value.CurrentModel}";

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
    /// Serialisation stub — intentionally writes nothing; API key is not persisted.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer) => true;

    /// <summary>
    /// Deserialisation stub — intentionally reads nothing; API key is not persisted.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IO.Serialization.GH_IReader reader) => true;
}