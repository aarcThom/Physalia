// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Physalia.Core.Config;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a resolved <see cref="ApiKey"/> (provider name plus key).
///
/// <para>The wire displays only "&lt;provider&gt; api key" — the secret is never shown
/// on the canvas and is never written to the GH file (<see cref="Write"/> is a no-op).
/// Keys originate solely from the API Keys component, which resolves them from
/// <c>API_KEY_CONFIG.YAML</c> / environment variables, so a plain-text key can never
/// enter a saved document.</para>
/// </summary>
public class GH_ApiKey : PhyGoo<GH_ApiKey, ApiKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ApiKey"/> class with no value.
    /// </summary>
    public GH_ApiKey()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ApiKey"/> class wrapping the given key.
    /// </summary>
    /// <param name="key">The resolved API key to wrap.</param>
    public GH_ApiKey(ApiKey key)
        : base(key)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "API Key";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "A resolved API key for a provider. Displays as a label only — the secret is never shown or serialised.";

    /// <inheritdoc/>
    public override string IsValidWhyNot => Value == null ? "No API key." : string.Empty;

    /// <inheritdoc/>
    public override string ToString() => Value == null ? "(no API key)" : $"{Value.Provider} api key";

    /// <inheritdoc/>
    /// <remarks>Strict: a key may only come from the API Keys component, never from a
    /// plain-text source, so a raw key can never be typed into a document.</remarks>
    public override bool CastFrom(object source)
    {
        switch (source)
        {
            case ApiKey key:
                Value = key;
                return true;
            case GH_ApiKey goo:
                Value = goo.Value;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Intentionally does not expose the key as text — casting out is limited to
    /// the safe "&lt;provider&gt; api key" label so the secret never lands on a panel or
    /// text input.</remarks>
    public override bool CastTo<Q>(ref Q target)
    {
        if (Value is not null && typeof(Q).IsAssignableFrom(typeof(GH_String)))
        {
            target = (Q)(object)new GH_String(ToString());
            return true;
        }

        return base.CastTo(ref target);
    }

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: the API key must never be persisted to disk.</remarks>
    public override bool Write(GH_IWriter writer) => true;

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: keys are recomputed from the API Keys component on each solve.</remarks>
    public override bool Read(GH_IReader reader) => true;
}
