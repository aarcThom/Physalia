// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base for components that let a signal THROUGH, or do not — a gate, a text switch, a throttle, a
/// hold. The conditional layer the pipeline was missing: until these existed the only branching in
/// Physalia was Detect JSON (which only knows about JSON) and Stall Guard (which only knows about
/// repeated failures), so "carry on only if…" had nowhere to live.
///
/// <para><b>The original signal is forwarded, never re-minted, and that is the load-bearing rule
/// here.</b> A signal carries Instructions on the Conversation Log to LLM Call hop — the trigger IS
/// the data — plus content blocks and an origin trail. Minting a replacement would drop all of it, so
/// a gate placed inline before an LLM Call would silently strip the conversation it was gating. The
/// sequence number travels too, which is what keeps causal order intact through the relay: a signal
/// does not become "newer" by being held.</para>
///
/// <para><b>One signal per solve, with a follow-up scheduled.</b> A relay may HOLD, so it cannot drain
/// its input in a single pass without the held one jumping the queue. Consuming the oldest and
/// scheduling another solve is lossless — the rest stay latched on the wire — and it also means each
/// forwarded signal gets its own solve on the output, so a downstream consumer sees them one at a time
/// in causal order rather than only the last of a batch.</para>
///
/// <para>At most ONE signal is ever held. Two policies cover every case built on this:
/// <see cref="HoldPolicy.KeepOldest"/> for a wait (the thing being waited for is that signal), and
/// <see cref="HoldPolicy.KeepNewest"/> for a throttle (an event superseded by a newer one is stale by
/// definition, and forwarding both would defeat the throttle).</para>
/// </summary>
public abstract class SignalRelayBase : StatefulComponentBase
{
    /// <summary>Index of the base-owned Signal input. Always first; subclass inputs follow it.</summary>
    protected const int InSignal = 0;

    /// <summary>Output index of the signal that got through.</summary>
    protected const int OutPassed = 0;

    private PhySignal? _passed;

    private PhySignal? _diverted;

    private PhySignal? _held;

    private bool _polling;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRelayBase"/> class in the Control Flow
    /// sub-category.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected SignalRelayBase(string name, string nickname, string description)
        : base(name, nickname, description, "Control Flow")
    {
    }

    /// <summary>
    /// What to do with one signal.
    /// </summary>
    protected enum RelayRoute
    {
        /// <summary>Send it out of the primary output.</summary>
        Pass,

        /// <summary>Send it out of the secondary output.</summary>
        Divert,

        /// <summary>Keep it and ask again later.</summary>
        Hold,

        /// <summary>Discard it: it goes nowhere and nothing downstream hears about it.</summary>
        Drop,
    }

    /// <summary>
    /// Which signal survives when a second one arrives while one is already held.
    /// </summary>
    protected enum HoldPolicy
    {
        /// <summary>
        /// The held signal stays and the arriving one waits its turn on the wire. For a wait, where
        /// the held signal is the one whose condition is being watched.
        /// </summary>
        KeepOldest,

        /// <summary>
        /// The arriving signal replaces the held one. For a throttle, where an event superseded by a
        /// newer event is stale, and forwarding both would be the opposite of throttling.
        /// </summary>
        KeepNewest,
    }

    /// <summary>
    /// Gets the index of the secondary output, or −1 when <see cref="HasSecondaryOutput"/> is false.
    /// </summary>
    protected int OutDiverted => HasSecondaryOutput ? 1 : -1;

    /// <summary>
    /// Gets the index of the first output registered by <see cref="RegisterAdditionalOutputs"/>.
    /// </summary>
    protected int FirstAdditionalOutputIndex => HasSecondaryOutput ? 2 : 1;

    /// <summary>Gets the signal currently held, or null when nothing is waiting.</summary>
    protected PhySignal? HeldSignal => _held;

    /// <summary>
    /// Gets the tooltip for the Signal input — what arriving here means for this particular relay.
    /// </summary>
    protected abstract string SignalInputDescription { get; }

    /// <summary>
    /// Gets the name of the primary output ("Passed", "Match", "Signal").
    /// </summary>
    protected abstract string PassedOutputName { get; }

    /// <summary>
    /// Gets the tooltip for the primary output.
    /// </summary>
    protected abstract string PassedOutputDescription { get; }

    /// <summary>
    /// Gets a value indicating whether this relay has a second output for signals it turns away.
    /// True for a gate or a switch, where knowing what was refused is half the point; false for a
    /// throttle, whose refused signals are superseded rather than routed.
    /// </summary>
    protected abstract bool HasSecondaryOutput { get; }

    /// <summary>
    /// Gets the name of the secondary output ("Blocked", "No Match"). Read only when
    /// <see cref="HasSecondaryOutput"/> is true.
    /// </summary>
    protected virtual string DivertedOutputName => "Blocked";

    /// <summary>
    /// Gets the tooltip for the secondary output. Read only when <see cref="HasSecondaryOutput"/> is
    /// true.
    /// </summary>
    protected virtual string DivertedOutputDescription => string.Empty;

    /// <summary>
    /// Gets which signal survives when one arrives while another is held. Only consulted by relays
    /// that ever return <see cref="RelayRoute.Hold"/>.
    /// </summary>
    protected virtual HoldPolicy Holding => HoldPolicy.KeepOldest;

    /// <summary>
    /// Gets how often, in milliseconds, to re-ask about a held signal when nothing else would cause a
    /// solve. Zero (the default) means never: a gate's condition arrives on a wire, so Grasshopper
    /// expires this component on its own when it changes. A relay waiting on something OUTSIDE the
    /// data graph — a file appearing, a clock — must poll, because nothing will come and tell it.
    /// </summary>
    protected virtual int PollMs => 0;

    /// <summary>Gets the caption shown under the component.</summary>
    protected abstract string RelayCaption { get; }

    /// <summary>
    /// Registers the relay's own inputs after the base-owned Signal input (index 1 onward).
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Registers outputs after the base-owned signal outputs.
    /// </summary>
    /// <param name="pManager">The output parameter manager.</param>
    protected virtual void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
    }

    /// <summary>
    /// Hook called at the top of every solve, before any routing. Read the relay's inputs here.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    protected virtual void OnSolveTick(IGH_DataAccess da)
    {
    }

    /// <summary>
    /// Hook called at the end of every solve. Override to publish extra outputs.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    protected virtual void OnSolveEnd(IGH_DataAccess da)
    {
    }

    /// <summary>
    /// Decides what to do with one signal. Called for a newly consumed signal and, on every later
    /// solve, for a signal being held — so a relay that holds must be able to answer the same question
    /// repeatedly about the same signal, and must not treat being asked again as a new event.
    /// </summary>
    /// <param name="signal">The signal to route.</param>
    /// <param name="held">True when this signal has been held since an earlier solve.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>What to do with it.</returns>
    protected abstract RelayRoute Decide(PhySignal signal, bool held, IGH_DataAccess da);

    /// <inheritdoc/>
    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", SignalInputDescription, GH_ParamAccess.list);
        pManager[InSignal].Optional = true;
        RegisterAdditionalInputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), PassedOutputName, "S", PassedOutputDescription, GH_ParamAccess.item);

        if (HasSecondaryOutput)
        {
            pManager.AddParameter(new Param_Signal(), DivertedOutputName, "B", DivertedOutputDescription, GH_ParamAccess.item);
        }

        RegisterAdditionalOutputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        OnSolveTick(DA);

        // Observe every solve, including while holding: signals arriving now wait on the wire and are
        // serviced once the held one has moved on. Nothing is dropped by being busy.
        ObserveSignalInputs(DA, InSignal);

        if (_held is { } waiting)
        {
            Route(waiting, Decide(waiting, held: true, DA), wasHeld: true);
        }

        if (_held is null && TryConsumeOldestSignal(InSignal, out PhySignal arrived))
        {
            Route(arrived, Decide(arrived, held: false, DA), wasHeld: false);
        }
        else if (_held is not null && Holding == HoldPolicy.KeepNewest && TryConsumeOldestSignal(InSignal, out PhySignal newer))
        {
            // Throttle semantics: the newest event is the only one worth forwarding, so it replaces
            // what is waiting rather than queueing behind it. Routed through Decide like any other
            // arrival, so the subclass sees the supersede happen and can count it — and it is
            // consumed either way, because an event deliberately overtaken has still been dealt with
            // and leaving it unconsumed would have it re-offered forever.
            Route(newer, Decide(newer, held: false, DA), wasHeld: false);
        }

        // NOTE: nothing is scheduled here for signals queued behind a hold. They are picked up by the
        // follow-up solve Route asks for when the hold clears, and asking on every solve instead would
        // be a busy loop wearing a timer's clothes — a relay with a queue holds for as long as its
        // condition says, which may be minutes.
        Message = RelayCaption;
        OnDisplayExpired(true);

        EmitSignal(DA, OutPassed, _passed);

        if (HasSecondaryOutput)
        {
            EmitSignal(DA, OutDiverted, _diverted);
        }

        OnSolveEnd(DA);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _passed = null;
        _diverted = null;
        _held = null;
        _polling = false;
    }

    /// <inheritdoc/>
    protected override void ClearStateOutputs()
    {
        _passed = null;
        _diverted = null;
    }

    private void Route(PhySignal signal, RelayRoute route, bool wasHeld)
    {
        switch (route)
        {
            case RelayRoute.Pass:
                _passed = signal;
                _held = null;
                break;

            case RelayRoute.Divert:
                _diverted = signal;
                _held = null;
                break;

            case RelayRoute.Hold:
                _held = signal;

                if (PollMs > 0)
                {
                    SchedulePoll();
                }

                break;

            case RelayRoute.Drop:
                _held = null;
                break;
        }

        if (wasHeld && route != RelayRoute.Hold)
        {
            // The hold has cleared, so anything queued behind it can be serviced. Asking for one more
            // solve is what makes the queue drain without waiting for an unrelated event.
            if (HasUnconsumedSignals(InSignal))
            {
                SchedulePoll();
            }
        }
    }

    // One outstanding poll at a time. Without the flag, a relay that holds through a hundred solves
    // would post a hundred scheduled solutions, all of which Grasshopper flushes at the next
    // solution — which is a busy loop wearing a timer's clothes.
    private void SchedulePoll()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        ScheduleStateSolve(PollMs > 0 ? PollMs : 1, () => _polling = false);
    }
}
