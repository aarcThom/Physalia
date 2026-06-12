// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Goo;

namespace Physalia.GH.Components;

/// <summary>
/// Base for components that participate in the Physalia data-flow lifecycle. Owns the
/// explicit solve state machine (<see cref="SolveState"/>), the canvas state caption,
/// sequenced-signal intake with consume-once semantics, latched outgoing signals, the
/// wall-clock-honest end-of-solve delay, and the Clear menu item. Nothing in the
/// lifecycle persists: signals are session events, so every component reopens Empty.
///
/// <para>This layer registers no parameters and has no <c>SolveInstance</c>; subclasses
/// drive the transitions from their own solve logic. Events travel as latched
/// <see cref="PhySignal"/> values, never as momentary pulses: a signal persists on the wire
/// and each receiver consumes it exactly once, keyed by its sequence number. Ordering is
/// defined by the global sequence — not by solve timing — so Grasshopper's single,
/// collapsing schedule timer can delay or coalesce solves without reordering, duplicating,
/// or dropping events. The visible delay is purely cosmetic; correctness never depends
/// on pacing.</para>
/// </summary>
public abstract class StatefulComponentBase : PhyBase
{
    /// <summary>
    /// Visible delay (milliseconds) between completion of a component's work and the
    /// success/failure latch, so the flow of data through the document can be traced
    /// by eye. Honoured against the wall clock even when the document schedule is
    /// flushed early. May be exposed to the user later.
    /// </summary>
    protected const int SolveDelayMs = 500;

    // Tolerance (ms) when deciding whether a scheduled callback fired early; roughly
    // the resolution of the underlying timer.
    private const int ScheduleSlopMs = 15;

    // Defensive cap on re-arms when the document keeps flushing the schedule early.
    private const int MaxScheduleAttempts = 100;

    // ---- consume-once intake state (per Signal input index; never serialised) ----
    private readonly Dictionary<int, long> _marks = new();
    private readonly Dictionary<int, List<PhySignal>> _wireSignals = new();
    private readonly Dictionary<int, List<bool>> _boolBaselines = new();
    private readonly Dictionary<int, List<PhySignal>> _pendingManual = new();
    private readonly HashSet<int> _observedOnce = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StatefulComponentBase"/> class.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    /// <param name="subCategory">Ribbon sub-category.</param>
    protected StatefulComponentBase(string name, string nickname, string description, string subCategory)
        : base(name, nickname, description, subCategory)
    {
    }

    /// <summary>
    /// Lifecycle state of a component, shown on the canvas via <see cref="GH_DocumentObject.Message"/>.
    /// </summary>
    protected enum SolveState
    {
        /// <summary>No data has passed through: fresh on canvas, never triggered, or manually cleared.</summary>
        Empty,

        /// <summary>Actively solving (including the visible end-of-solve delay). Outputs are blank.</summary>
        Active,

        /// <summary>The last run succeeded; the success signal (carrying the result payload) is latched until the next run or a clear.</summary>
        SolveSuccess,

        /// <summary>The last run failed; the failure signal (carrying the feedback payload) is latched until the next run or a clear.</summary>
        SolveFailure,
    }

    /// <summary>
    /// A signal consumed from a specific input, so callers processing several inputs at
    /// once know where each event arrived.
    /// </summary>
    /// <param name="ParamIndex">The input parameter index the signal arrived on.</param>
    /// <param name="Signal">The consumed signal.</param>
    protected readonly record struct ConsumedSignal(int ParamIndex, PhySignal Signal);

    /// <summary>Gets the current lifecycle state.</summary>
    protected SolveState State { get; private set; } = SolveState.Empty;

    /// <summary>
    /// Gets the latched success signal, minted by <see cref="LatchSuccess"/>. Persists on
    /// the wire until the next <see cref="EnterActive"/> or a clear; downstream consumers
    /// fire on it exactly once.
    /// </summary>
    protected PhySignal? SuccessSignal { get; private set; }

    /// <summary>
    /// Gets the latched failure signal, minted by <see cref="LatchFailure"/>. Persists on
    /// the wire until the next <see cref="EnterActive"/> or a clear.
    /// </summary>
    protected PhySignal? FailSignal { get; private set; }

    /// <summary>
    /// Caption for the menu item that clears the component back to <see cref="SolveState.Empty"/>.
    /// </summary>
    protected virtual string ClearMenuText => "Clear Outputs";

    /// <summary>
    /// Wipes any latched output backing fields beyond the signals themselves (which the
    /// base clears). Called when a run starts (<see cref="EnterActive"/>) and by the menu
    /// Clear. Must not touch domain state (e.g. a conversation log) — use
    /// <see cref="OnCleared"/> for that. Default implementation does nothing.
    /// </summary>
    protected virtual void ClearStateOutputs()
    {
    }

    /// <summary>
    /// Extra reset work performed by the menu Clear only (lifecycle flags, domain data).
    /// Consume-once bookkeeping is deliberately NOT reset — clearing outputs must never
    /// replay already-consumed events. Default implementation does nothing.
    /// </summary>
    protected virtual void OnCleared()
    {
    }

    /// <summary>
    /// Canvas caption for a state. Defaults: blank / "Active…" / "Success" / "Failed".
    /// </summary>
    /// <param name="state">The state to caption.</param>
    /// <returns>The caption shown under the component.</returns>
    protected virtual string MessageForState(SolveState state) => state switch
    {
        SolveState.Active => "Active…",
        SolveState.SolveSuccess => "Success",
        SolveState.SolveFailure => "Failed",
        _ => string.Empty,
    };

    /// <summary>
    /// Reads the given Signal inputs and updates the consume-once bookkeeping. Call once
    /// at the top of every solve, for every Signal input, regardless of
    /// <see cref="State"/> — observing while Active is what makes events lossless
    /// (they wait, latched on the wire or queued from a Button press, until consumed).
    /// Idempotent within a solve.
    ///
    /// <para>Genuine signals are snapshot as candidates; a wire holds one latched signal
    /// per source, so two rapid events from the same source supersede (latest wins).
    /// Plain-bool sources (Buttons/Toggles, via the <see cref="GH_Signal"/> cast sentinel)
    /// are edge-detected here: each false→true transition mints exactly one signal into a
    /// pending queue. The first ever observation of an input baselines it — pre-existing
    /// latched signals and stuck-true Toggles never fire on a fresh, pasted, or reloaded
    /// component. Anything else wired in (text, numbers, …) raises an error rather than
    /// being silently ignored.</para>
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    /// <param name="paramIndices">The Signal input parameter indices to observe.</param>
    protected void ObserveSignalInputs(IGH_DataAccess da, params int[] paramIndices)
    {
        foreach (int idx in paramIndices)
        {
            var items = new List<GH_Signal>();
            da.GetDataList(idx, items);

            var wire = new List<PhySignal>();
            var boolLevels = new List<bool>();

            foreach (GH_Signal? item in items)
            {
                if (item?.Value is PhySignal signal)
                {
                    wire.Add(signal);
                }
                else if (item?.BoolLevel is bool level)
                {
                    boolLevels.Add(level);
                }
                else
                {
                    // A null/empty item means a non-signal source was wired in (the
                    // Signal cast accepts only signals and bool levels). Fail loudly:
                    // silently ignoring it would look like a dropped event.
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        $"\"{Params.Input[idx].Name}\" accepts only Signals (native Buttons/Toggles also work). Use Construct Signal to turn text into a signal.");
                }
            }

            bool first = _observedOnce.Add(idx);
            _wireSignals[idx] = wire;

            if (first)
            {
                // First-observation baseline: swallow whatever is already on the wire so a
                // fresh/pasted/reloaded component never fires off pre-existing state.
                _marks[idx] = wire.Count > 0 ? wire.Max(s => s.Sequence) : 0;
                _boolBaselines[idx] = boolLevels;
                continue;
            }

            List<bool> baseline = _boolBaselines.TryGetValue(idx, out var b) ? b : new List<bool>();
            if (baseline.Count != boolLevels.Count)
            {
                // Wiring changed (bool source added/removed): re-baseline silently.
                _boolBaselines[idx] = boolLevels;
                continue;
            }

            for (int i = 0; i < boolLevels.Count; i++)
            {
                if (boolLevels[i] && !baseline[i])
                {
                    // One minted signal per false→true transition (Button press), even mid-Active.
                    PendingManualFor(idx).Add(PhySignal.Mint(
                        SignalOutcome.Success, string.Empty, InstanceGuid, $"{Name} (manual)"));
                }
            }

            _boolBaselines[idx] = boolLevels;
        }
    }

    /// <summary>
    /// Whether any observed signal on the given inputs is still unconsumed. Peeks only;
    /// advances nothing. Use after a latch to decide whether to schedule a follow-up
    /// solve that services events which arrived mid-run.
    /// </summary>
    /// <param name="paramIndices">The Signal input parameter indices to check.</param>
    /// <returns>true when at least one unconsumed signal is waiting.</returns>
    protected bool HasUnconsumedSignals(params int[] paramIndices) =>
        paramIndices.Any(idx => UnconsumedFor(idx).Any());

    /// <summary>
    /// Consumes the single oldest unconsumed signal on the given input, by global
    /// sequence order. Newer signals stay unconsumed for later runs.
    /// </summary>
    /// <param name="paramIndex">The Signal input parameter index.</param>
    /// <param name="signal">The consumed signal, when one was waiting.</param>
    /// <returns>true when a signal was consumed.</returns>
    protected bool TryConsumeOldestSignal(int paramIndex, out PhySignal signal)
    {
        PhySignal? oldest = UnconsumedFor(paramIndex).OrderBy(s => s.Sequence).FirstOrDefault();
        if (oldest is null)
        {
            signal = default!;
            return false;
        }

        MarkConsumed(paramIndex, oldest);
        signal = oldest;
        return true;
    }

    /// <summary>
    /// Consumes every unconsumed signal across the given inputs, returned in global
    /// sequence order. Sequence order is causal order — an event minted as a consequence
    /// of another always sorts after it — so processing the result in order is what
    /// guarantees, e.g., that a response is recorded before the feedback it provoked,
    /// even when both land in the same solve.
    /// </summary>
    /// <param name="paramIndices">The Signal input parameter indices to drain.</param>
    /// <returns>All consumed signals with their input of origin, oldest first.</returns>
    protected IReadOnlyList<ConsumedSignal> ConsumeAllSignals(params int[] paramIndices)
    {
        var consumed = new List<ConsumedSignal>();
        foreach (int idx in paramIndices)
        {
            foreach (PhySignal s in UnconsumedFor(idx).ToList())
            {
                MarkConsumed(idx, s);
                consumed.Add(new ConsumedSignal(idx, s));
            }
        }

        consumed.Sort((a, b) => a.Signal.Sequence.CompareTo(b.Signal.Sequence));
        return consumed;
    }

    /// <summary>
    /// Enters <see cref="SolveState.Active"/>: clears the latched outputs and both
    /// outgoing signals so stale data leaves the wires the moment a run starts.
    /// </summary>
    protected void EnterActive()
    {
        State = SolveState.Active;
        SuccessSignal = null;
        FailSignal = null;
        ClearStateOutputs();
        UpdateStateDisplay();
    }

    /// <summary>
    /// Enters <see cref="SolveState.SolveSuccess"/> and (unless quiet) mints the latched
    /// success signal. The caller must have latched its data output beforehand.
    /// </summary>
    /// <param name="payload">The event payload carried by the minted signal (the data string).</param>
    /// <param name="emitSignal">
    /// When false, state and caption update but no signal is minted — a quiet success
    /// that must not fire downstream (e.g. Recorder recording an assistant turn).
    /// </param>
    /// <param name="outcome">
    /// Outcome stamped on the minted signal. Defaults to Success; pass-through components
    /// (e.g. Feedback Collector) may forward a Failure outcome for trace truthfulness even
    /// though their own run succeeded.
    /// </param>
    protected void LatchSuccess(string payload, bool emitSignal = true, SignalOutcome outcome = SignalOutcome.Success)
    {
        State = SolveState.SolveSuccess;
        SuccessSignal = emitSignal ? PhySignal.Mint(outcome, payload, InstanceGuid, Name) : null;
        FailSignal = null;
        UpdateStateDisplay();
    }

    /// <summary>
    /// Enters <see cref="SolveState.SolveFailure"/> and (unless quiet) mints the latched
    /// failure signal. The caller must have latched its feedback output beforehand.
    /// </summary>
    /// <param name="payload">The event payload carried by the minted signal (the feedback string).</param>
    /// <param name="emitSignal">When false, state and caption update but no signal is minted.</param>
    protected void LatchFailure(string payload, bool emitSignal = true)
    {
        State = SolveState.SolveFailure;
        FailSignal = emitSignal ? PhySignal.Mint(SignalOutcome.Failure, payload, InstanceGuid, Name) : null;
        SuccessSignal = null;
        UpdateStateDisplay();
    }

    /// <summary>
    /// Returns to <see cref="SolveState.Empty"/> and drops both outgoing signals. Used by
    /// the menu Clear and by aborted runs. Does not call <see cref="ClearStateOutputs"/> —
    /// callers decide whether outputs need wiping (an aborted run already cleared them on
    /// <see cref="EnterActive"/>).
    /// </summary>
    protected void ResetToEmpty()
    {
        State = SolveState.Empty;
        SuccessSignal = null;
        FailSignal = null;
        UpdateStateDisplay();
    }

    /// <summary>
    /// Single scheduling funnel for lifecycle solves (read passes, the end-of-solve delay,
    /// follow-up consumption checks, aborts). Honest against the wall clock: Grasshopper
    /// keeps ONE document schedule and flushes every pending callback at the next solution,
    /// so a callback that fires early re-arms itself for the remainder of its delay instead
    /// of acting. Runs <paramref name="onScheduled"/> once the due time has genuinely
    /// passed, then expires this component. Safe to call from a background thread.
    /// </summary>
    /// <param name="delayMs">Minimum wall-clock delay in milliseconds.</param>
    /// <param name="onScheduled">State mutation to apply when the scheduled solution starts.</param>
    protected void ScheduleStateSolve(int delayMs, Action onScheduled)
    {
        ScheduleAt(DateTime.UtcNow.AddMilliseconds(delayMs), onScheduled, attempt: 0);
    }

    /// <summary>
    /// Pushes <see cref="MessageForState"/> for the current state to the canvas caption.
    /// Called by every transition; safe to call from subclass code after a Read.
    /// </summary>
    protected void UpdateStateDisplay()
    {
        Message = MessageForState(State);
        OnDisplayExpired(true);
    }

    /// <summary>
    /// Emits a latched signal on an output, leaving the wire genuinely empty when there
    /// is none (skips SetData rather than emitting a null item).
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    /// <param name="outputIndex">The output parameter index.</param>
    /// <param name="signal">The latched signal, or null for none.</param>
    protected static void EmitSignal(IGH_DataAccess da, int outputIndex, PhySignal? signal)
    {
        if (signal is not null)
        {
            da.SetData(outputIndex, new GH_Signal(signal));
        }
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, ClearMenuText, (_, _) =>
        {
            ClearStateOutputs();
            OnCleared();
            ResetToEmpty();
            ExpireSolution(true);
        });
    }

    private void ScheduleAt(DateTime due, Action onScheduled, int attempt)
    {
        int delay = Math.Max(1, (int)Math.Ceiling((due - DateTime.UtcNow).TotalMilliseconds));
        OnPingDocument()?.ScheduleSolution(delay, _ =>
        {
            double remaining = (due - DateTime.UtcNow).TotalMilliseconds;
            if (remaining > ScheduleSlopMs && attempt < MaxScheduleAttempts)
            {
                // The document schedule was flushed early by another solution; re-arm for
                // the remainder so the wall-clock delay is honoured.
                ScheduleAt(due, onScheduled, attempt + 1);
                return;
            }

            onScheduled();
            ExpireSolution(true);
        });
    }

    private IEnumerable<PhySignal> UnconsumedFor(int paramIndex)
    {
        long mark = _marks.TryGetValue(paramIndex, out long m) ? m : 0;

        IEnumerable<PhySignal> fromWire = _wireSignals.TryGetValue(paramIndex, out var wire)
            ? wire.Where(s => s.Sequence > mark)
            : Enumerable.Empty<PhySignal>();

        return _pendingManual.TryGetValue(paramIndex, out var pending)
            ? fromWire.Concat(pending)
            : fromWire;
    }

    private void MarkConsumed(int paramIndex, PhySignal signal)
    {
        if (_pendingManual.TryGetValue(paramIndex, out var pending) && pending.Remove(signal))
        {
            return;
        }

        long mark = _marks.TryGetValue(paramIndex, out long m) ? m : 0;
        _marks[paramIndex] = Math.Max(mark, signal.Sequence);
    }

    private List<PhySignal> PendingManualFor(int paramIndex)
    {
        if (!_pendingManual.TryGetValue(paramIndex, out var pending))
        {
            pending = new List<PhySignal>();
            _pendingManual[paramIndex] = pending;
        }

        return pending;
    }
}
