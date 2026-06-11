// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base for components that route a result either forward (Data + Success Signal) or
/// back as Feedback (Feedback + Fail Signal). Owns all I/O registration, output latching,
/// and save/load serialisation; the lifecycle state machine, signal intake/emission, and
/// Clear menu item come from <see cref="StatefulComponentBase"/>. Subclasses supply only
/// the Data input type and the per-component processing logic.
///
/// <para>Consuming a signal on the base-owned <c>Signal</c> input starts a run, split
/// across solves. The <see cref="PushSolve"/> pass performs side effects (e.g. pushing
/// data into a linked component and expiring it); the base then defers a
/// <see cref="ReadSolve"/> pass that produces the routing result once the document has
/// settled. Once the work is done, the base holds
/// <see cref="StatefulComponentBase.SolveDelayMs"/> before latching, so the data hop is
/// visible on the canvas. Signals arriving while a run is in flight wait, latched on the
/// wire, and are serviced by a follow-up solve after the latch — no event is ever
/// dropped. Synchronous components leave <see cref="PushSolve"/> empty; asynchronous ones
/// set <see cref="AutoScheduleRead"/> to false and call <see cref="RequestReadPass"/>
/// when their work completes.</para>
/// </summary>
/// <typeparam name="TData">Type produced from the Data input and handed to the solve passes.</typeparam>
public abstract class RoutingComponentBase<TData> : StatefulComponentBase
{
    /// <summary>Output index of the latched Data string.</summary>
    protected const int OutData = 0;

    /// <summary>Output index of the latched Success Signal.</summary>
    protected const int OutSuccessSignal = 1;

    /// <summary>Output index of the latched Feedback string.</summary>
    protected const int OutFeedback = 2;

    /// <summary>Output index of the latched Fail Signal.</summary>
    protected const int OutFailSignal = 3;

    /// <summary>
    /// ScheduleSolution delay (milliseconds) before the read pass runs. Gives the
    /// document time to settle the push pass's side effects. Bump if a downstream
    /// component's state is not yet updated when <see cref="ReadSolve"/> runs.
    /// </summary>
    private const int ReadDelayMs = 1;

    /// <summary>
    /// Maximum number of times the read pass is re-scheduled when <see cref="IsReadReady"/>
    /// returns false, before the run latches a failure. Guards against a target that never
    /// settles (e.g. a linked component that is locked or was deleted mid-run).
    /// </summary>
    private const int MaxReadRetries = 10;

    private string _dataOut = string.Empty;
    private string _feedbackOut = string.Empty;

    // Index of the base-owned Signal input (appended last during registration).
    private int _signalIndex = -1;

    // Data captured on the push pass, handed to ReadSolve on the deferred read pass.
    private TData _pushData = default!;

    // Push -> read handshake. _awaitingRead means a push pass has run and a read pass
    // is pending. _doRead is set ONLY by our own scheduled callback so that arbitrary
    // intervening solves never trigger the read early.
    private bool _awaitingRead;
    private bool _doRead;

    // The visible end-of-solve delay has elapsed; the next read solve performs the latch.
    private bool _delayElapsed;

    // IsReadReady retries were exhausted; the latch records a timeout failure.
    private bool _readTimedOut;

    // Number of read-pass attempts deferred because IsReadReady returned false.
    private int _readRetries;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingComponentBase{TData}"/> class.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    /// <param name="subCategory">Ribbon sub-category.</param>
    protected RoutingComponentBase(string name, string nickname, string description, string subCategory)
        : base(name, nickname, description, subCategory)
    {
    }

    /// <summary>
    /// Registers the subclass-typed Data input (index 0).
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected abstract void RegisterDataInput(GH_InputParamManager pManager);

    /// <summary>
    /// Registers any inputs after Data (e.g. a Schema input).
    /// Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Reads the Data input. Returns false when the input is absent or empty, in which
    /// case a consumed signal is dropped with a warning (there is nothing to process).
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    /// <param name="data">The parsed Data value when present.</param>
    /// <returns>true if Data is present and non-empty; otherwise false.</returns>
    protected abstract bool TryGetData(IGH_DataAccess da, out TData data);

    /// <summary>
    /// First pass. Performs side effects that must settle before the result is read
    /// (e.g. pushing code into a linked component and expiring it). Runs when a signal
    /// is consumed, before the deferred <see cref="ReadSolve"/> pass. Leave empty for
    /// components that compute their result synchronously and need no settle pass.
    /// </summary>
    /// <param name="data">The Data value read by <see cref="TryGetData"/>.</param>
    /// <param name="da">The data access for the current solve.</param>
    protected abstract void PushSolve(TData data, IGH_DataAccess da);

    /// <summary>
    /// Second pass. Produces the routing result after the document has re-solved
    /// following <see cref="PushSolve"/>. Read any additional inputs directly from
    /// <paramref name="da"/>.
    /// </summary>
    /// <param name="data">The Data value captured on the push pass.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>A success result carrying the Data string, or a failure result carrying Feedback.</returns>
    protected abstract RoutingResult ReadSolve(TData data, IGH_DataAccess da);

    /// <summary>
    /// Whether the push pass's side effects have settled enough for <see cref="ReadSolve"/>
    /// to run (e.g. a linked component has re-solved). When false the base re-schedules the
    /// read pass, up to <see cref="MaxReadRetries"/> attempts; exhausting them latches a
    /// failure. Default implementation returns true.
    /// </summary>
    /// <param name="data">The Data value captured on the push pass.</param>
    /// <returns>true when the read pass may run; false to retry on a later solution.</returns>
    protected virtual bool IsReadReady(TData data) => true;

    /// <summary>
    /// Whether the base auto-schedules the read pass immediately after <see cref="PushSolve"/>.
    /// Override to return false for asynchronous components (e.g. an LLM call) that must
    /// instead call <see cref="RequestReadPass"/> when their work completes.
    /// </summary>
    protected virtual bool AutoScheduleRead => true;

    /// <summary>
    /// Hook called at the very start of every solve, before any signal or read-pass logic.
    /// Override to read per-solve inputs the routing lifecycle does not (e.g. a Cancel input).
    /// Default implementation does nothing.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    protected virtual void OnSolveTick(IGH_DataAccess da)
    {
    }

    /// <inheritdoc/>
    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        RegisterDataInput(pManager);
        RegisterAdditionalInputs(pManager);
        _signalIndex = pManager.AddParameter(
            new Param_Signal(),
            "Signal",
            "S",
            "Run signal. Each incoming signal runs the component exactly once; multiple sources may be wired directly. Native Buttons/Toggles also work (one run per press).",
            GH_ParamAccess.list);
        pManager[_signalIndex].Optional = true;
    }

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Data", "D", "Latched result on success. Persists until the next run or a clear.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Signal(), "Success Signal", "SS", "Latched signal minted when a run succeeds. Downstream components consume it exactly once.", GH_ParamAccess.item);
        pManager.AddTextParameter("Feedback", "F", "Latched feedback on failure. Persists until the next run or a clear.", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Signal(), "Fail Signal", "FS", "Latched signal minted when a run fails. Downstream components consume it exactly once.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        OnSolveTick(DA);

        // Observe every solve, even mid-run: signals arriving while busy stay latched on
        // the wire (or queue from a Button press) and are serviced after the latch.
        ObserveSignalInputs(DA, _signalIndex);

        if (_awaitingRead && _doRead)
        {
            _doRead = false;

            if (!_delayElapsed)
            {
                bool ready = IsReadReady(_pushData);
                if (!ready && _readRetries < MaxReadRetries)
                {
                    // Side effects have not settled yet (e.g. the linked component has not
                    // re-solved). Defer the read pass to a later solution.
                    _readRetries++;
                    RequestReadPass();
                    Emit(DA);
                    return;
                }

                // Work is done (or retries are exhausted). Hold the visible delay once
                // before latching so the data hop can be traced on the canvas.
                _readTimedOut = !ready;
                ScheduleStateSolve(SolveDelayMs, () =>
                {
                    _delayElapsed = true;
                    _doRead = true;
                });
                Emit(DA);
                return;
            }

            // LATCH PASS — runs only from our own scheduled signal, after the visible delay.
            _awaitingRead = false;
            _delayElapsed = false;
            _readRetries = 0;

            RoutingResult result = _readTimedOut
                ? RoutingResult.Fail(
                    "The linked component never re-solved, so no result could be read back.",
                    "Read pass timed out waiting for push side effects to settle.",
                    GH_RuntimeMessageLevel.Error)
                : ReadSolve(_pushData, DA);
            _readTimedOut = false;

            if (result.Message != null)
            {
                AddRuntimeMessage(result.MessageLevel, result.Message);
            }

            if (result.Success)
            {
                _dataOut = result.Output;
                _feedbackOut = string.Empty;
                LatchSuccess(_dataOut);
            }
            else
            {
                _dataOut = string.Empty;
                _feedbackOut = result.Output;
                LatchFailure(_feedbackOut);
            }

            if (HasUnconsumedSignals(_signalIndex))
            {
                // A signal arrived mid-run; nothing was dropped. One follow-up solve
                // starts the next run (oldest signal first).
                ScheduleStateSolve(1, () => { });
            }

            Emit(DA);
            return;
        }

        if (!_awaitingRead && TryConsumeOldestSignal(_signalIndex, out _))
        {
            // PUSH PASS — a consumed signal starts a run. Side effects only; no result yet.
            if (!TryGetData(DA, out TData data))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Signal received but the Data input is empty; nothing to process.");
                Emit(DA);
                return;
            }

            _pushData = data;
            EnterActive();
            PushSolve(data, DA);
            _awaitingRead = true;
            _doRead = false;
            _delayElapsed = false;
            _readTimedOut = false;

            if (AutoScheduleRead)
            {
                RequestReadPass();
            }

            Emit(DA);
            return;
        }

        // Idle solve — re-emit the latched outputs and signals (same sequence numbers;
        // downstream consume-once means recomputes never re-fire a chain).
        Emit(DA);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        if (StringHelpers.IsNonBlank(_dataOut))
        {
            writer.SetString("DataOut", _dataOut);
        }

        if (StringHelpers.IsNonBlank(_feedbackOut))
        {
            writer.SetString("FeedbackOut", _feedbackOut);
        }

        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _dataOut = reader.ItemExists("DataOut") ? reader.GetString("DataOut") : string.Empty;
        _feedbackOut = reader.ItemExists("FeedbackOut") ? reader.GetString("FeedbackOut") : string.Empty;

        bool ok = base.Read(reader);

        if (!reader.ItemExists("State"))
        {
            // Legacy file written before the explicit state machine: infer the latched
            // state from the persisted payloads.
            RestorePersistedState(
                StringHelpers.IsNonBlank(_dataOut) ? SolveState.SolveSuccess
                : StringHelpers.IsNonBlank(_feedbackOut) ? SolveState.SolveFailure
                : SolveState.Empty);
        }

        return ok;
    }

    private void Emit(IGH_DataAccess da)
    {
        da.SetData(OutData, _dataOut);
        EmitSignal(da, OutSuccessSignal, SuccessSignal);
        da.SetData(OutFeedback, _feedbackOut);
        EmitSignal(da, OutFailSignal, FailSignal);
    }

    /// <summary>
    /// Schedules the deferred read pass. The base calls this automatically after
    /// <see cref="PushSolve"/> when <see cref="AutoScheduleRead"/> is true; asynchronous
    /// subclasses call it themselves when their work completes. Safe to call from a
    /// background thread — the read is marshalled onto a scheduled solution.
    /// </summary>
    protected void RequestReadPass()
    {
        ScheduleStateSolve(ReadDelayMs, () => _doRead = true);
    }

    /// <summary>
    /// Cancels a pending read pass without latching a result or minting a signal. Use when
    /// an in-flight asynchronous run is aborted so the component returns to
    /// <see cref="StatefulComponentBase.SolveState.Empty"/>. Any signals that arrived
    /// mid-run are consumed naturally on the solve this schedules.
    /// </summary>
    protected void AbortReadPass()
    {
        ScheduleStateSolve(ReadDelayMs, () =>
        {
            _awaitingRead = false;
            _doRead = false;
            _delayElapsed = false;
            _readTimedOut = false;
            _readRetries = 0;
            ResetToEmpty();
        });
    }

    /// <inheritdoc/>
    protected override void ClearStateOutputs()
    {
        _dataOut = string.Empty;
        _feedbackOut = string.Empty;
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _awaitingRead = false;
        _doRead = false;
        _delayElapsed = false;
        _readTimedOut = false;
        _readRetries = 0;
    }

    /// <summary>
    /// Outcome of a <see cref="ReadSolve"/> call: either a forward-routed Data string
    /// or a back-routed Feedback string, plus an optional runtime message.
    /// </summary>
    protected readonly record struct RoutingResult
    {
        private RoutingResult(bool success, string output, string? message, GH_RuntimeMessageLevel messageLevel)
        {
            Success = success;
            Output = output;
            Message = message;
            MessageLevel = messageLevel;
        }

        /// <summary>Gets a value indicating whether the run succeeded.</summary>
        public bool Success { get; }

        /// <summary>Gets the Data string on success, or the Feedback string on failure.</summary>
        public string Output { get; }

        /// <summary>Gets an optional runtime message to surface on the component.</summary>
        public string? Message { get; }

        /// <summary>Gets the level for <see cref="Message"/>.</summary>
        public GH_RuntimeMessageLevel MessageLevel { get; }

        /// <summary>
        /// Creates a success result carrying the forward-routed Data string.
        /// </summary>
        /// <param name="data">The Data string to latch and emit.</param>
        /// <returns>A success <see cref="RoutingResult"/>.</returns>
        public static RoutingResult Ok(string data) =>
            new(true, data, null, GH_RuntimeMessageLevel.Blank);

        /// <summary>
        /// Creates a failure result carrying the back-routed Feedback string.
        /// </summary>
        /// <param name="feedback">The Feedback string to latch and emit.</param>
        /// <param name="message">An optional runtime message to surface.</param>
        /// <param name="level">The level for the runtime message.</param>
        /// <returns>A failure <see cref="RoutingResult"/>.</returns>
        public static RoutingResult Fail(string feedback, string? message = null, GH_RuntimeMessageLevel level = GH_RuntimeMessageLevel.Warning) =>
            new(false, feedback, message, level);
    }
}
