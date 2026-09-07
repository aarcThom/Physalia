// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.GH.Parameters;
using Rhino;

namespace Physalia.GH.Components;

/// <summary>
/// Base for components that START a round from something OUTSIDE the pipeline — a clock, a folder,
/// the Rhino document, data arriving from the canvas. The event tier the plug-in did not have: until
/// these existed a round could only begin because a human typed in the chat, pressed a Button through
/// Construct Signal, or a Feedback loop re-entered, so every pipeline was downstream of somebody
/// being present.
///
/// <para><b>A source has no Signal input and no visible delay.</b> It is where events come from, not
/// a hop they pass through, so it latches on the solve its own wake-up schedules — the same shape as
/// Construct Signal, whose Button is the manual member of this family.</para>
///
/// <para><b>Arming is session-only and is never serialized, deliberately.</b> Opening a file, pasting
/// a harness, or loading a preset must not start spending money on a machine nobody is watching, and
/// a colleague opening a shared pipeline must not have it begin calling a model at them. That is the
/// same reasoning as the first-observation baselining that stops a stuck Toggle firing on load, and
/// the same reasoning as nothing else in the lifecycle persisting. So a trigger always reopens
/// <c>off</c> and someone has to arm it — from the node's own menu, or the harness panel's
/// disarm-everything button, which is the other half of the same decision.</para>
///
/// <para><b>Bursts are coalesced, and this is not a nicety.</b> One copied folder raises one
/// file-system event per file; one saved file raises several for that file; a script adding five
/// hundred objects raises five hundred Rhino events. Firing per event would start a round per event.
/// So events are accumulated and a settle timer restarts on each one, and only when the burst has
/// been quiet for <see cref="SettleMs"/> is a single signal minted, carrying whatever
/// <see cref="ComposePayload"/> makes of the whole batch.</para>
///
/// <para><b>Waking a document that is not being solved.</b> Grasshopper drops scheduled solutions on
/// a disabled document, and a harness sub-document's <c>Enabled</c> flag is Physalia's own invariant
/// (the proxy re-asserts it on every solve) rather than a user setting — but the proxy only solves
/// when the host does, and an autonomous trigger fires when nothing has solved for hours. So the flag
/// is re-asserted here before scheduling, for a harness document ONLY: on the user's own file that
/// flag is Grasshopper's solver lock, and overriding it would restart a solver the user switched off
/// on purpose.</para>
/// </summary>
/// <typeparam name="TEvent">
/// What one external event is, in whatever shape the subclass needs to describe a batch of them
/// afterwards: a file-system change, a kind-of-Rhino-change flag, a tick. Strings would do for some
/// of these and not for the folder watcher, whose coalescing rules need the structured events.
/// </typeparam>
public abstract class SignalSourceBase<TEvent> : StatefulComponentBase, IArmableTrigger
{
    /// <summary>Output index of the minted Signal. Always first; subclass outputs follow it.</summary>
    protected const int OutSignal = 0;

    /// <summary>
    /// Gets the index of the first output registered by <see cref="RegisterAdditionalOutputs"/>: 1,
    /// after the Signal output.
    /// </summary>
    protected static int FirstAdditionalOutputIndex => 1;

    private readonly object _gate = new();

    private readonly List<TEvent> _pending = new();

    // Session-only, never serialized. See the class remarks: a file must never open armed.
    private bool _armed;

    private System.Threading.Timer? _settle;

    // Set only by our own scheduled callback, so an unrelated intervening solve never fires.
    private bool _doFire;

    private int _fired;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalSourceBase{TEvent}"/> class in the Triggers
    /// sub-category.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected SignalSourceBase(string name, string nickname, string description)
        : base(name, nickname, description, "Triggers")
    {
    }

    /// <summary>
    /// Gets a value indicating whether this trigger is currently armed. False on every fresh, pasted,
    /// or reloaded component.
    /// </summary>
    public bool IsArmed => _armed;

    /// <summary>
    /// Gets how many signals this trigger has minted since it was placed. Shown in the caption, so an
    /// armed trigger that has never fired is told apart from one that is firing constantly.
    /// </summary>
    protected int FiredCount => _fired;

    /// <summary>
    /// Gets the tooltip for this trigger's Signal output — what the payload says and where it usually
    /// goes. Each subclass writes its own: the whole point of a trigger is WHICH event it is, and a
    /// shared default would say nothing about that.
    /// </summary>
    protected abstract string SignalOutputDescription { get; }

    /// <summary>
    /// Gets the caption shown while armed — the settings that matter, in a few characters
    /// ("every 30s", "*.las"). Read on every state display, so it may reflect live input values.
    /// </summary>
    protected abstract string ArmedCaption { get; }

    /// <summary>
    /// Gets how long a burst must be quiet before one signal is minted for it. Override to read an
    /// input; the default suits a watcher reporting file-system or document events.
    /// </summary>
    protected virtual int SettleMs => 250;

    /// <summary>
    /// Composes the payload for one coalesced batch of events. Return an empty string to mint NOTHING
    /// — the correct answer when the batch cancelled itself out (a temporary file that appeared and
    /// vanished), which is a real case and not an error.
    /// </summary>
    /// <param name="events">The events accumulated during the burst, oldest first.</param>
    /// <returns>The payload the minted signal carries, or an empty string to fire nothing.</returns>
    protected abstract string ComposePayload(IReadOnlyList<TEvent> events);

    /// <summary>
    /// Starts whatever this trigger listens to. Called when the user arms it, and again on any solve
    /// where the settings it depends on have changed. Must be safe to call when already listening.
    /// </summary>
    protected abstract void StartListening();

    /// <summary>
    /// Stops listening and releases anything held. Called on disarm, on removal from the document, and
    /// before <see cref="StartListening"/> re-arms with different settings. Must be safe to call when
    /// not listening.
    /// </summary>
    protected abstract void StopListening();

    /// <summary>
    /// Registers the trigger's own inputs. There is no base-owned input: a source has nothing wired
    /// into it, which is what makes it a source. Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterSourceInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Registers outputs after the base-owned Signal output (<see cref="FirstAdditionalOutputIndex"/>
    /// onward). Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The output parameter manager.</param>
    protected virtual void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
    }

    /// <summary>
    /// Hook called at the top of every solve, before any fire logic. Read the trigger's inputs here
    /// and re-arm the listener if what it depends on has changed. Default implementation does nothing.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    protected virtual void OnSolveTick(IGH_DataAccess da)
    {
    }

    /// <summary>
    /// Hook called at the end of every solve, after any fire. Override to publish extra outputs.
    /// Default implementation does nothing.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    protected virtual void OnSolveEnd(IGH_DataAccess da)
    {
    }

    /// <summary>
    /// Reports one external event. Safe to call from any thread — a file-system watcher's thread pool
    /// callback, a Rhino document event, a timer — which is the whole reason this funnel exists.
    /// Accumulates the event and restarts the settle window; nothing is minted until the burst goes
    /// quiet. Ignored outright while disarmed, so a listener that has not shut down yet cannot fire.
    /// </summary>
    /// <param name="e">The event to report.</param>
    protected void ReportEvent(TEvent e)
    {
        lock (_gate)
        {
            if (!_armed)
            {
                return;
            }

            _pending.Add(e);

            // Restarts rather than accumulates: the window is "quiet for this long", so a burst of a
            // thousand events is one wake-up whose length is the burst's, not a thousand wake-ups.
            _settle?.Dispose();
            _settle = new System.Threading.Timer(
                _ => Wake(),
                null,
                Math.Max(1, SettleMs),
                System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>
    /// Arms or disarms the trigger and refreshes the caption. Public so the harness panel's
    /// disarm-everything button can reach it.
    /// </summary>
    /// <param name="on">True to start listening; false to stop and drop anything pending.</param>
    public void SetArmed(bool on)
    {
        lock (_gate)
        {
            if (_armed == on)
            {
                return;
            }

            _armed = on;

            if (!on)
            {
                // Disarming drops the batch as well as the listener. A trigger switched off mid-burst
                // must not fire the burst it was switched off during.
                _pending.Clear();
                _settle?.Dispose();
                _settle = null;
                _doFire = false;
            }
        }

        if (on)
        {
            StartListening();
        }
        else
        {
            StopListening();
        }

        UpdateStateDisplay();
        ExpireSolution(true);
    }

    /// <inheritdoc/>
    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        RegisterSourceInputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Signal", "S", SignalOutputDescription, GH_ParamAccess.item);
        RegisterAdditionalOutputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        OnSolveTick(DA);

        if (_doFire)
        {
            // FIRE PASS — runs only from our own scheduled callback, once a burst has settled.
            _doFire = false;
            Fire();
        }

        EmitSignal(DA, OutSignal, SuccessSignal);
        OnSolveEnd(DA);
    }

    /// <inheritdoc/>
    protected override string MessageForState(SolveState state)
    {
        // A source's caption is about what it is LISTENING for, not how its last solve went: "off" and
        // "every 30s · 4" are the two things a reader of this node wants, and "Success" is neither.
        if (!_armed)
        {
            return "off";
        }

        string detail = ArmedCaption;
        string count = _fired > 0 ? $" · {_fired}" : string.Empty;
        return string.IsNullOrWhiteSpace(detail) ? $"armed{count}" : $"{detail}{count}";
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(
            menu,
            "Armed",
            (_, _) => SetArmed(!_armed),
            enabled: true,
            @checked: _armed);
    }

    /// <inheritdoc/>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        TriggerRegistry.Register(this);
        UpdateStateDisplay();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Disarms rather than only unsubscribing: a deleted trigger holding a live watcher would keep
    /// firing into a component that is no longer on any document.
    /// </remarks>
    public override void RemovedFromDocument(GH_Document document)
    {
        SetArmedQuietly(false);
        TriggerRegistry.Unregister(this);
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        lock (_gate)
        {
            _pending.Clear();
            _doFire = false;
        }

        _fired = 0;
    }

    /// <summary>
    /// Re-arms the listener when the settings it depends on have changed, without touching the armed
    /// flag. Call from <see cref="OnSolveTick"/> after reading inputs.
    /// </summary>
    protected void RestartListening()
    {
        if (!_armed)
        {
            return;
        }

        StopListening();
        StartListening();
    }

    // Disarm without a re-solve: for removal, where asking for a solution on a document we are
    // leaving is neither useful nor safe.
    private void SetArmedQuietly(bool on)
    {
        lock (_gate)
        {
            _armed = on;
            _pending.Clear();
            _settle?.Dispose();
            _settle = null;
            _doFire = false;
        }

        StopListening();
    }

    // Called on the settle timer's thread. Hops to the UI thread before touching the document, the
    // way the Project Folder grounder's watcher does: Grasshopper's document is not ours to schedule
    // against from a thread pool callback.
    private void Wake()
    {
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            lock (_gate)
            {
                if (!_armed || _pending.Count == 0)
                {
                    return;
                }

                _settle?.Dispose();
                _settle = null;
            }

            // Ready() re-enables a harness sub-document whose proxy has not solved for a while,
            // without touching the solver lock on a user's own file. See PipelineWake for why a
            // wake-up is otherwise dropped in silence.
            if (PipelineWake.Ready(this) is null)
            {
                return;
            }

            ScheduleStateSolve(1, () => _doFire = true);
        }));
    }

    private void Fire()
    {
        List<TEvent> batch;
        lock (_gate)
        {
            if (!_armed || _pending.Count == 0)
            {
                // Disarmed between the schedule and the solve, or already drained. Either way there is
                // nothing to fire, and minting an empty signal would start a round about nothing.
                return;
            }

            batch = new List<TEvent>(_pending);
            _pending.Clear();
        }

        string payload = ComposePayload(batch);
        if (string.IsNullOrEmpty(payload))
        {
            // A batch that cancelled itself out (a temporary file that came and went). Composing to
            // nothing is the documented way for a subclass to say "no event after all".
            UpdateStateDisplay();
            return;
        }

        _fired++;
        LatchSuccess(payload);
    }
}
