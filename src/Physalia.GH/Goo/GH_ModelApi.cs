// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Physalia.Core.Config;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a resolved <see cref="ModelApi"/> — one provider's endpoint and
/// credential travelling together as a single value.
/// </summary>
/// <remarks>
/// <para>Replaces <c>GH_ApiKey</c>, which carried a key alone. An endpoint and the key valid at it
/// are one fact, and separating them made the OpenAI-compatible node ask for a URL the user had to
/// know by heart.</para>
/// <para>The wire displays only "&lt;provider&gt; api" — neither the secret nor the endpoint is
/// shown on the canvas, and nothing is written to the GH file (<see cref="Write"/> is a no-op).
/// Values originate solely from the Model API component, which resolves them from the encrypted
/// credential store or the environment, so a plain-text key can never enter a saved document.</para>
/// </remarks>
public class GH_ModelApi : PhyGoo<GH_ModelApi, ModelApi>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ModelApi"/> class with no value.
    /// </summary>
    public GH_ModelApi()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ModelApi"/> class wrapping the given value.
    /// </summary>
    /// <param name="api">The resolved endpoint and credential to wrap.</param>
    public GH_ModelApi(ModelApi api)
        : base(api)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "Model API";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "A provider's endpoint and key, together. Displays as a label only — neither is shown on the canvas or saved into your file.";

    /// <inheritdoc/>
    public override string IsValidWhyNot => this.Value == null ? "No model API." : string.Empty;

    /// <inheritdoc/>
    public override string ToString() => this.Value == null ? "(no model API)" : $"{this.Value.Provider} api";

    /// <inheritdoc/>
    /// <remarks>
    /// Strict: a value may only come from the Model API component, never from a plain-text source,
    /// so neither a key nor an endpoint can be typed into a document.
    /// </remarks>
    public override bool CastFrom(object source)
    {
        switch (source)
        {
            case ModelApi api:
                this.Value = api;
                return true;
            case GH_ModelApi goo:
                this.Value = goo.Value;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Intentionally does not expose the key or the endpoint as text — casting out is limited to
    /// the safe "&lt;provider&gt; api" label so neither lands on a panel or a text input.
    /// </remarks>
    public override bool CastTo<Q>(ref Q target)
    {
        if (this.Value is not null && typeof(Q).IsAssignableFrom(typeof(GH_String)))
        {
            target = (Q)(object)new GH_String(this.ToString());
            return true;
        }

        return base.CastTo(ref target);
    }

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: the credential must never be persisted to disk.</remarks>
    public override bool Write(GH_IWriter writer) => true;

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: values are re-resolved by the Model API component on each solve.</remarks>
    public override bool Read(GH_IReader reader) => true;
}
