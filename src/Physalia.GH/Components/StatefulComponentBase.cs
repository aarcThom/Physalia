// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace Physalia.GH.Components;

/// <summary>
/// Base for components that participate in the Physalia data-flow lifecycle. Owns the
/// explicit solve state machine (<see cref="SolveState"/>), the canvas state caption,
/// trigger rising-edge detection, momentary success/fail pulses, the visible end-of-solve
/// delay, the Clear menu item, and state serialisation.
///
/// <para>This layer registers no parameters and has no <c>SolveInstance</c>; subclasses
/// drive the transitions from their own solve logic. <see cref="EnterActive"/> clears the
/// latched outputs (via <see cref="ClearStateOutputs"/>) at the start of a run;
/// <see cref="LatchSuccess"/> / <see cref="LatchFailure"/> end it, optionally pulsing the
/// matching trigger for exactly one solve. Schedule the latch through
/// <see cref="ScheduleStateSolve"/> with <see cref="SolveDelayMs"/> so a human watching the
/// canvas can trace data hopping through the DAG.</para>
/// </summary>
public abstract class StatefulComponentBase : PhyBase
{
    /// <summary>
    /// Visible delay (milliseconds) between completion of a component's work and the
    /// success/failure latch, so the flow of data through the document can be traced
    /// by eye. May be exposed to the user later.
    /// </summary>
    protected const int SolveDelayMs = 500;

    // Rising-edge tracking for the Trigger input; serialised so a reload doesn't fire.
    private bool _lastTrigger;

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

        /// <summary>The last run succeeded; the Data output is latched until the next run or a clear.</summary>
        SolveSuccess,

        /// <summary>The last run failed; the Feedback output is latched until the next run or a clear.</summary>
        SolveFailure,
    }

    /// <summary>Gets the current lifecycle state.</summary>
    protected SolveState State { get; private set; } = SolveState.Empty;

    /// <summary>Gets a value indicating whether the success trigger is pulsing on this solve.</summary>
    protected bool SuccessPulse { get; private set; }

    /// <summary>Gets a value indicating whether the fail trigger is pulsing on this solve.</summary>
    protected bool FailPulse { get; private set; }

    /// <summary>
    /// Caption for the menu item that clears the component back to <see cref="SolveState.Empty"/>.
    /// </summary>
    protected virtual string ClearMenuText => "Clear Outputs";

    /// <summary>
    /// Whether a persisted <see cref="SolveState.SolveSuccess"/> / <see cref="SolveState.SolveFailure"/>
    /// survives a file reload. Return false when the latched payload itself is not serialised,
    /// so the component reopens as <see cref="SolveState.Empty"/> instead of claiming a result
    /// it no longer holds.
    /// </summary>
    protected virtual bool RestoreLatchedStateOnLoad => true;

    /// <summary>
    /// Wipes the latched output backing fields only. Called when a run starts
    /// (<see cref="EnterActive"/>) and by the menu Clear. Must not touch domain state
    /// (e.g. a conversation log) — use <see cref="OnCleared"/> for that.
    /// </summary>
    protected abstract void ClearStateOutputs();

    /// <summary>
    /// Extra reset work performed by the menu Clear only (lifecycle flags, domain data).
    /// Default implementation does nothing.
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
    /// Updates the serialised last-trigger value and reports whether this solve saw a
    /// rising edge (false → true) on the Trigger input.
    /// </summary>
    /// <param name="trigger">The Trigger input value for this solve.</param>
    /// <returns>true on a rising edge; otherwise false.</returns>
    protected bool DetectRisingEdge(bool trigger)
    {
        bool rising = trigger && !_lastTrigger;
        _lastTrigger = trigger;
        return rising;
    }

    /// <summary>
    /// Enters <see cref="SolveState.Active"/>: clears the latched outputs and both pulses
    /// so stale data leaves the wires the moment a run starts.
    /// </summary>
    protected void EnterActive()
    {
        State = SolveState.Active;
        SuccessPulse = false;
        FailPulse = false;
        ClearStateOutputs();
        UpdateStateDisplay();
    }

    /// <summary>
    /// Enters <see cref="SolveState.SolveSuccess"/>. The caller must have latched its data
    /// output beforehand.
    /// </summary>
    /// <param name="pulse">
    /// When true the success trigger pulses for exactly one solve, then resets.
    /// Pass false for a quiet success that updates state without firing downstream
    /// (e.g. Recorder recording an assistant turn).
    /// </param>
    protected void LatchSuccess(bool pulse = true)
    {
        State = SolveState.SolveSuccess;
        SuccessPulse = pulse;
        FailPulse = false;
        UpdateStateDisplay();

        if (pulse)
        {
            SchedulePulseReset();
        }
    }

    /// <summary>
    /// Enters <see cref="SolveState.SolveFailure"/>. The caller must have latched its
    /// feedback output beforehand.
    /// </summary>
    /// <param name="pulse">
    /// When true the fail trigger pulses for exactly one solve, then resets.
    /// Pass false for a quiet failure that updates state without firing downstream.
    /// </param>
    protected void LatchFailure(bool pulse = true)
    {
        State = SolveState.SolveFailure;
        FailPulse = pulse;
        SuccessPulse = false;
        UpdateStateDisplay();

        if (pulse)
        {
            SchedulePulseReset();
        }
    }

    /// <summary>
    /// Returns to <see cref="SolveState.Empty"/> without firing a pulse. Used by the menu
    /// Clear and by aborted runs. Does not call <see cref="ClearStateOutputs"/> — callers
    /// decide whether outputs need wiping (an aborted run already cleared them on
    /// <see cref="EnterActive"/>).
    /// </summary>
    protected void ResetToEmpty()
    {
        State = SolveState.Empty;
        SuccessPulse = false;
        FailPulse = false;
        UpdateStateDisplay();
    }

    /// <summary>
    /// Single scheduling funnel for lifecycle solves (read passes, the end-of-solve delay,
    /// pulse resets, aborts). Runs <paramref name="onScheduled"/> on the scheduled solution,
    /// then expires this component. Safe to call from a background thread.
    /// </summary>
    /// <param name="delayMs">ScheduleSolution delay in milliseconds.</param>
    /// <param name="onScheduled">State mutation to apply when the scheduled solution starts.</param>
    protected void ScheduleStateSolve(int delayMs, Action onScheduled)
    {
        OnPingDocument()?.ScheduleSolution(delayMs, _ =>
        {
            onScheduled();
            ExpireSolution(true);
        });
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
    /// Restores a persisted state without running transition side effects. For use from
    /// <see cref="Read"/> paths only (e.g. legacy-file state inference).
    /// </summary>
    /// <param name="state">The state to restore.</param>
    protected void RestorePersistedState(SolveState state)
    {
        State = state;
        UpdateStateDisplay();
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

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("State", (int)State);
        writer.SetBoolean("LastTrigger", _lastTrigger);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _lastTrigger = reader.ItemExists("LastTrigger") && reader.GetBoolean("LastTrigger");

        if (reader.ItemExists("State"))
        {
            var persisted = (SolveState)reader.GetInt32("State");

            // In-flight work is unrecoverable, and latched results only survive when the
            // payload itself was serialised. Pulses are never persisted.
            State = persisted switch
            {
                SolveState.SolveSuccess or SolveState.SolveFailure when RestoreLatchedStateOnLoad => persisted,
                _ => SolveState.Empty,
            };
        }
        else
        {
            State = SolveState.Empty;
        }

        SuccessPulse = false;
        FailPulse = false;
        UpdateStateDisplay();
        return base.Read(reader);
    }

    private void SchedulePulseReset()
    {
        ScheduleStateSolve(1, () =>
        {
            SuccessPulse = false;
            FailPulse = false;
        });
    }
}
