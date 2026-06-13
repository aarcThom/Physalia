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
/// <para>A Signal wire carries only genuine signals: casting accepts a <see cref="PhySignal"/>
/// (or another <see cref="GH_Signal"/>) and nothing else. A native bool source (Button /
/// Toggle) does NOT cast — it has no payload, so it is rejected at the input. Manual runs go
/// through Construct Signal's dedicated Boolean trigger instead.</para>
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

    /// <inheritdoc/>
    public override string TypeName => "Signal";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "A sequence-numbered event. Latches on the wire; downstream components consume each signal exactly once.";

    /// <inheritdoc/>
    public override bool IsValid => Value is not null;

    /// <inheritdoc/>
    public override IGH_Goo Duplicate() => new GH_Signal { Value = Value };

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Value is null)
        {
            return "(empty signal)";
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
            target = (Q)(object)new GH_Boolean(Value is not null);
            return true;
        }

        if (typeof(Q).IsAssignableFrom(typeof(bool)))
        {
            target = (Q)(object)(Value is not null);
            return true;
        }

        // The payload escape hatch: a Signal wire plugs straight into any native text
        // input and yields the carried payload. Genuine signals only — a bool sentinel
        // has no payload to give.
        if (Value is not null && typeof(Q).IsAssignableFrom(typeof(GH_String)))
        {
            target = (Q)(object)new GH_String(Value.Payload);
            return true;
        }

        if (Value is not null && typeof(Q).IsAssignableFrom(typeof(string)))
        {
            target = (Q)(object)Value.Payload;
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
