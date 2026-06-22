// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.ConvoInstruct;
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
    protected const int SolveDelayMs = 20;

    // Tolerance (ms) when deciding whether a scheduled callback fired early; roughly
    // the resolution of the underlying timer.
    private const int ScheduleSlopMs = 15;

    // Defensive cap on re-arms when the document keeps flushing the schedule early.
    private const int MaxScheduleAttempts = 100;

    // ---- consume-once intake state (per Signal input index; never serialised) ----
    private readonly Dictionary<int, long> _marks = new();
    private readonly Dictionary<int, List<PhySignal>> _wireSignals = new();
    private readonly HashSet<int> _observedOnce = new();

    // ---- native bool-trigger edge-detection state (per Boolean input index; never serialised) ----
    private readonly Dictionary<int, bool> _buttonLevels = new();
    private readonly HashSet<int> _buttonObserved = new();

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
    /// Gets a value indicating whether the component is currently Active (a run is in
    /// flight, including the visible end-of-solve delay). Public so other components'
    /// attributes can reflect pipeline activity (e.g. Prompter's busy animation).
    /// </summary>
    public bool IsBusy => State == SolveState.Active;

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
    /// (they wait, latched on the wire, until consumed). Idempotent within a solve.
    ///
    /// <para>A Signal input carries only genuine signals. Each wire holds one latched signal
    /// per source, so two rapid events from the same source supersede (latest wins). The
    /// first ever observation of an input baselines it — pre-existing latched signals never
    /// fire on a fresh, pasted, or reloaded component. A null or empty item on the wire is
    /// tolerated silently (an unset or passthrough-empty Signal input is normal); only a
    /// genuinely foreign source — a bool (Button/Toggle), text, numbers, geometry, anything
    /// that is not a Signal — raises an error, caught by inspecting the source goo types so
    /// an empty wire is never mistaken for one. Manual runs come from Construct Signal, whose
    /// dedicated Boolean trigger mints a payload-carrying signal.</para>
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

            foreach (GH_Signal? item in items)
            {
                if (item?.Value is PhySignal signal)
                {
                    wire.Add(signal);
                }

                // A null or empty item is tolerated: an unset or passthrough-empty Signal
                // wire is normal. A failed cast from foreign data also lands here as a
                // null, so it is NOT flagged from the item — HasForeignSignalSource below
                // inspects the source goo (which keeps its original type) to tell the two
                // apart and error only on the genuinely foreign one.
            }

            if (HasForeignSignalSource(idx))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"\"{Params.Input[idx].Name}\" accepts only Signals. Use Construct Signal to turn text (and a Button trigger) into a signal.");
            }

            bool first = _observedOnce.Add(idx);
            _wireSignals[idx] = wire;

            if (first)
            {
                // First-observation baseline: swallow whatever is already on the wire so a
                // fresh/pasted/reloaded component never fires off pre-existing state.
                _marks[idx] = wire.Count > 0 ? wire.Max(s => s.Sequence) : 0;
            }
        }
    }

    /// <summary>
    /// Reads a native Boolean trigger input and reports a press — a false→true transition
    /// since the last observation. The manual-run path for source components (e.g. Construct
    /// Signal): a Button/Toggle plugs into a plain Boolean parameter, never a Signal input.
    /// The first ever observation baselines the input, so a stuck-true Toggle on a fresh,
    /// pasted, or reloaded component never fires. Session-only state, never serialised.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    /// <param name="boolParamIndex">The Boolean input parameter index to observe.</param>
    /// <returns>true exactly once per false→true press; otherwise false.</returns>
    protected bool ObserveButtonPress(IGH_DataAccess da, int boolParamIndex)
    {
        bool level = false;
        da.GetData(boolParamIndex, ref level);

        bool firstObservation = _buttonObserved.Add(boolParamIndex);
        bool previous = _buttonLevels.TryGetValue(boolParamIndex, out bool p) && p;
        _buttonLevels[boolParamIndex] = level;

        return !firstObservation && level && !previous;
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
    /// <param name="contentBlocks">
    /// Optional resolved content blocks carried alongside the payload — e.g. a Prompter user
    /// turn with inline images. Null/empty for the common text-only case.
    /// </param>
    protected void LatchSuccess(string payload, bool emitSignal = true, SignalOutcome outcome = SignalOutcome.Success, IReadOnlyList<MessageContent>? contentBlocks = null)
    {
        State = SolveState.SolveSuccess;
        SuccessSignal = emitSignal ? PhySignal.Mint(outcome, payload, InstanceGuid, Name, contentBlocks) : null;
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

    /// <summary>
    /// Whether a non-signal source is wired into the given input. Inspects the source
    /// goo directly (it keeps its original type even after a failed cast to Signal), so a
    /// genuinely foreign type — a bool (Button/Toggle), text, numbers, geometry — is told
    /// apart from a benign null/empty item, which the failed cast would otherwise also leave
    /// on the wire. Only a genuine signal is accepted; a bare bool has no payload and is
    /// rejected (Construct Signal's Boolean trigger is the manual path).
    /// </summary>
    /// <param name="paramIndex">The Signal input parameter index.</param>
    /// <returns>true when at least one wired source produces non-signal data.</returns>
    private bool HasForeignSignalSource(int paramIndex)
    {
        foreach (IGH_Param source in Params.Input[paramIndex].Sources)
        {
            foreach (IGH_Goo? goo in source.VolatileData.AllData(true))
            {
                if (goo is null || !goo.IsValid)
                {
                    // Empty/null source data — tolerated, never an error.
                    continue;
                }

                if (goo is GH_Signal)
                {
                    // A genuine signal — accepted.
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private IEnumerable<PhySignal> UnconsumedFor(int paramIndex)
    {
        long mark = _marks.TryGetValue(paramIndex, out long m) ? m : 0;

        return _wireSignals.TryGetValue(paramIndex, out var wire)
            ? wire.Where(s => s.Sequence > mark)
            : Enumerable.Empty<PhySignal>();
    }

    private void MarkConsumed(int paramIndex, PhySignal signal)
    {
        long mark = _marks.TryGetValue(paramIndex, out long m) ? m : 0;
        _marks[paramIndex] = Math.Max(mark, signal.Sequence);
    }
}
