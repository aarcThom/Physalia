// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Physalia.Core.Signals;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a <see cref="PhySignal"/>.
///
/// <para>Signals are ephemeral session events: they are never serialised, so a reopened
/// file has signal-free wires and nothing re-fires on load.</para>
///
/// <para>Casting from a plain bool (native Button / Toggle) does NOT mint a signal — casts
/// run during every data collection, so minting here would re-fire downstream every solve
/// while a Toggle is stuck true. Instead the cast captures the raw level in
/// <see cref="BoolLevel"/>; the consuming component edge-detects against its own per-input
/// state and mints exactly one signal per false→true transition.</para>
/// </summary>
public class GH_Signal : PhyGoo<GH_Signal, PhySignal>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_Signal"/> class with no value.
    /// </summary>
    public GH_Signal()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_Signal"/> class wrapping the given signal.
    /// </summary>
    /// <param name="signal">The signal to wrap.</param>
    public GH_Signal(PhySignal signal)
        : base(signal)
    {
    }

    /// <summary>
    /// Gets the raw boolean level captured from a foreign bool source (Button/Toggle).
    /// Set only by <see cref="CastFrom"/>; null for genuine signals. Consuming components
    /// edge-detect on this to mint one signal per press.
    /// </summary>
    public bool? BoolLevel { get; private set; }

    /// <inheritdoc/>
    public override string TypeName => "Signal";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "A sequence-numbered event. Latches on the wire; downstream components consume each signal exactly once.";

    /// <inheritdoc/>
    public override bool IsValid => Value is not null || BoolLevel.HasValue;

    /// <inheritdoc/>
    public override IGH_Goo Duplicate() => new GH_Signal { Value = Value, BoolLevel = BoolLevel };

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Value is null)
        {
            return BoolLevel.HasValue ? $"(bool {(BoolLevel.Value ? "true" : "false")})" : "(empty signal)";
        }

        string preview = Value.Payload.Length > 40 ? Value.Payload[..40] + "…" : Value.Payload;
        preview = preview.Replace('\r', ' ').Replace('\n', ' ');
        string payloadPart = preview.Length > 0 ? $" — \"{preview}\"" : string.Empty;
        return $"#{Value.Sequence} {Value.Outcome} from {Value.SourceName} @ {Value.Timestamp.ToLocalTime():HH:mm:ss.fff}{payloadPart}";
    }

    /// <inheritdoc/>
    public override bool CastFrom(object source)
    {
        switch (source)
        {
            case PhySignal signal:
                Value = signal;
                return true;
            case GH_Signal goo:
                Value = goo.Value;
                BoolLevel = goo.BoolLevel;
                return true;
            case bool level:
                Value = null;
                BoolLevel = level;
                return true;
            case GH_Boolean ghBool:
                Value = null;
                BoolLevel = ghBool.Value;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public override bool CastTo<Q>(ref Q target)
    {
        // Interop nicety: a latched signal reads as level-true on native bool inputs
        // (not a pulse — it stays true while latched).
        if (typeof(Q).IsAssignableFrom(typeof(GH_Boolean)))
        {
            target = (Q)(object)new GH_Boolean(Value is not null || BoolLevel == true);
            return true;
        }

        if (typeof(Q).IsAssignableFrom(typeof(bool)))
        {
            target = (Q)(object)(Value is not null || BoolLevel == true);
            return true;
        }

        return base.CastTo(ref target);
    }

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: signals are ephemeral session events and never persist.</remarks>
    public override bool Write(GH_IWriter writer) => true;

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: signals are ephemeral session events and never persist.</remarks>
    public override bool Read(GH_IReader reader) => true;
}
