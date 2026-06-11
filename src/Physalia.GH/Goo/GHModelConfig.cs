// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Models;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper wrapper for a <see cref="ModelConfig"/> instance.
/// API keys are never written to the GH file — the component recomputes from its inputs.
/// </summary>
public class GH_ModelConfig : PhyGoo<GH_ModelConfig, ModelConfig>
{
    /// <summary>
    /// Initializes a new empty instance of the <see cref="GH_ModelConfig"/> class.
    /// </summary>
    public GH_ModelConfig()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ModelConfig"/> class with the given config.
    /// </summary>
    /// <param name="config">The model configuration to wrap.</param>
    public GH_ModelConfig(ModelConfig config)
        : base(config)
    {
    }

    /// <inheritdoc/>
    public override string IsValidWhyNot => Value == null ? "No model configuration." : string.Empty;

    /// <inheritdoc/>
    public override string TypeName => "Model Config";

    /// <inheritdoc/>
    public override string TypeDescription => "An LLM model configuration (provider, model ID, API key, inference parameters).";

    /// <inheritdoc/>
    public override string ToString() => Value == null ? "Null" : Value.ModelId;

    /// <summary>
    /// Intentionally writes nothing — API key must not be persisted to disk.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer) => true;

    /// <summary>
    /// Intentionally reads nothing — config is recomputed from component inputs on each solve.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IO.Serialization.GH_IReader reader) => true;
}
