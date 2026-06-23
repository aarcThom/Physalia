// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base for components that route a result either forward (Success Signal) or back
/// (Fail Signal). The signal is the only carrier between pipeline components: its
/// payload holds the result string on success or the feedback string on failure, so the
/// contract is one wire per hop. The lifecycle state machine, signal intake/emission,
/// and Clear menu item come from <see cref="StatefulComponentBase"/>. Subclasses supply
/// only their extra inputs and the per-component processing logic.
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
    /// <summary>Output index of the latched Success Signal.</summary>
    protected const int OutSuccessSignal = 0;

    /// <summary>Output index of the latched Fail Signal.</summary>
    protected const int OutFailSignal = 1;

    /// <summary>
    /// Gets the latched aux signal, set when <see cref="ReadSolve"/> returns
    /// <see cref="RoutingResult.Aux"/>. Re-emitted on <see cref="AuxOutputIndex"/> every solve
    /// (like Success/Fail) so downstream consume-once stays reliable. Null unless a subclass
    /// opts into a third output.
    /// </summary>
    protected PhySignal? AuxSignal { get; private set; }

    /// <summary>
    /// Output index of the optional third "aux" route a subclass adds via
    /// <see cref="RegisterAdditionalOutputs"/>. Default −1 means no aux output, so the base
    /// emits only Success/Fail and behaves exactly as before for every existing component.
    /// </summary>
    protected virtual int AuxOutputIndex => -1;

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
    /// Registers the subclass's inputs (e.g. a Schema input), starting at index 0.
    /// The base appends the Signal input last. Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Registers extra outputs after the fixed Success(0)/Fail(1) signals. A subclass that
    /// adds one here must also override <see cref="AuxOutputIndex"/> to its index so the base
    /// re-emits the latched aux signal there. Default implementation adds nothing.
    /// </summary>
    /// <param name="pManager">The output parameter manager.</param>
    protected virtual void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
    }

    /// <summary>
    /// Produces the working data for a run from the consumed signal and/or the
    /// component's own inputs. Most components take the signal's payload; components
    /// whose context arrives on a typed input (e.g. Reasoner's Instructions) read that
    /// instead. Returns false when nothing usable is available, in which case the
    /// consumed signal is dropped with a warning (there is nothing to process).
    /// </summary>
    /// <param name="signal">The consumed signal that starts this run.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <param name="data">The working data when available.</param>
    /// <returns>true if there is data to process; otherwise false.</returns>
    protected abstract bool TryGetData(PhySignal signal, IGH_DataAccess da, out TData data);

    /// <summary>
    /// First pass. Performs side effects that must settle before the result is read
    /// (e.g. pushing code into a linked component and expiring it). Runs when a signal
    /// is consumed, before the deferred <see cref="ReadSolve"/> pass. Leave empty for
    /// components that compute their result synchronously and need no settle pass.
    /// </summary>
    /// <param name="data">The working data produced by <see cref="TryGetData"/>.</param>
    /// <param name="da">The data access for the current solve.</param>
    protected abstract void PushSolve(TData data, IGH_DataAccess da);

    /// <summary>
    /// Second pass. Produces the routing result after the document has re-solved
    /// following <see cref="PushSolve"/>. Read any additional inputs directly from
    /// <paramref name="da"/>.
    /// </summary>
    /// <param name="data">The working data captured on the push pass.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>A success result carrying the result string, or a failure result carrying feedback.</returns>
    protected abstract RoutingResult ReadSolve(TData data, IGH_DataAccess da);

    /// <summary>
    /// Whether the push pass's side effects have settled enough for <see cref="ReadSolve"/>
    /// to run (e.g. a linked component has re-solved). When false the base re-schedules the
    /// read pass, up to <see cref="MaxReadRetries"/> attempts; exhausting them latches a
    /// failure. Default implementation returns true.
    /// </summary>
    /// <param name="data">The working data captured on the push pass.</param>
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
        RegisterAdditionalInputs(pManager);
        _signalIndex = pManager.AddParameter(
            new Param_Signal(),
            "Signal",
            "S",
            "Run signal. Each incoming signal runs the component exactly once; multiple signal sources may be wired directly. For a manual run, wire a Construct Signal (Button + payload).",
            GH_ParamAccess.list);
        pManager[_signalIndex].Optional = true;
    }

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Success Signal", "SS", "Latched signal minted when a run succeeds; its payload carries the result. Downstream components consume it exactly once. Casts to text (the payload).", GH_ParamAccess.item);
        pManager.AddParameter(new Param_Signal(), "Fail Signal", "FS", "Latched signal minted when a run fails; its payload carries the feedback. Downstream components consume it exactly once. Casts to text (the payload).", GH_ParamAccess.item);
        RegisterAdditionalOutputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        OnSolveTick(DA);

        // Observe every solve, even mid-run: signals arriving while busy stay latched on
        // the wire and are serviced after the latch.
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

            if (result.IsBroadcast)
            {
                // A terminal result routed identically down both outputs: latch the one
                // pre-minted signal on Success and Fail so either downstream branch receives
                // the same payload and content (e.g. a message plus a viewport snapshot).
                LatchBroadcast(result.BroadcastSignal!);
            }
            else if (result.IsAux)
            {
                // A third-route result: latch quietly (no Success/Fail signal) and stash the
                // pre-minted aux signal; Emit re-emits it on AuxOutputIndex.
                LatchSuccess(string.Empty, emitSignal: false);
                AuxSignal = result.AuxSignal;
            }
            else if (result.Success)
            {
                LatchSuccess(result.Output);
            }
            else
            {
                LatchFailure(result.Output);
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

        if (!_awaitingRead && TryConsumeOldestSignal(_signalIndex, out PhySignal trigger))
        {
            // PUSH PASS — a consumed signal starts a run. Side effects only; no result yet.
            if (!TryGetData(trigger, DA, out TData data))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Signal carried no payload and no data is available; nothing to process.");
                Emit(DA);
                return;
            }

            _pushData = data;
            EnterActive();
            AuxSignal = null;
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

        // Idle solve — re-emit the latched signals (same sequence numbers; downstream
        // consume-once means recomputes never re-fire a chain).
        Emit(DA);
    }

    private void Emit(IGH_DataAccess da)
    {
        EmitSignal(da, OutSuccessSignal, SuccessSignal);
        EmitSignal(da, OutFailSignal, FailSignal);

        if (AuxOutputIndex >= 0)
        {
            EmitSignal(da, AuxOutputIndex, AuxSignal);
        }
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
            AuxSignal = null;
            ResetToEmpty();
        });
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _awaitingRead = false;
        _doRead = false;
        _delayElapsed = false;
        _readTimedOut = false;
        _readRetries = 0;
        AuxSignal = null;
    }

    /// <summary>
    /// Outcome of a <see cref="ReadSolve"/> call: either a forward-routed result string
    /// or a back-routed feedback string (each becomes the minted signal's payload), plus
    /// an optional runtime message.
    /// </summary>
    protected readonly record struct RoutingResult
    {
        private RoutingResult(bool success, string output, string? message, GH_RuntimeMessageLevel messageLevel, PhySignal? auxSignal, PhySignal? broadcastSignal)
        {
            Success = success;
            Output = output;
            Message = message;
            MessageLevel = messageLevel;
            AuxSignal = auxSignal;
            BroadcastSignal = broadcastSignal;
        }

        /// <summary>Gets a value indicating whether the run succeeded.</summary>
        public bool Success { get; }

        /// <summary>Gets the result string on success, or the feedback string on failure.</summary>
        public string Output { get; }

        /// <summary>Gets an optional runtime message to surface on the component.</summary>
        public string? Message { get; }

        /// <summary>Gets the level for <see cref="Message"/>.</summary>
        public GH_RuntimeMessageLevel MessageLevel { get; }

        /// <summary>
        /// Gets the pre-minted signal to emit on the aux output, or null for a Success/Fail result.
        /// </summary>
        public PhySignal? AuxSignal { get; }

        /// <summary>Gets a value indicating whether this result routes to the aux output.</summary>
        public bool IsAux => AuxSignal is not null;

        /// <summary>
        /// Gets the pre-minted signal to latch identically on both the Success and Fail outputs,
        /// or null for a normal Success/Fail/Aux result.
        /// </summary>
        public PhySignal? BroadcastSignal { get; }

        /// <summary>Gets a value indicating whether this result broadcasts to both outputs.</summary>
        public bool IsBroadcast => BroadcastSignal is not null;

        /// <summary>
        /// Creates a success result carrying the forward-routed result string.
        /// </summary>
        /// <param name="data">The result string carried by the minted success signal.</param>
        /// <returns>A success <see cref="RoutingResult"/>.</returns>
        public static RoutingResult Ok(string data) =>
            new(true, data, null, GH_RuntimeMessageLevel.Blank, null, null);

        /// <summary>
        /// Creates a failure result carrying the back-routed feedback string.
        /// </summary>
        /// <param name="feedback">The feedback string carried by the minted fail signal.</param>
        /// <param name="message">An optional runtime message to surface.</param>
        /// <param name="level">The level for the runtime message.</param>
        /// <returns>A failure <see cref="RoutingResult"/>.</returns>
        public static RoutingResult Fail(string feedback, string? message = null, GH_RuntimeMessageLevel level = GH_RuntimeMessageLevel.Warning) =>
            new(false, feedback, message, level, null, null);

        /// <summary>
        /// Creates a result that emits a caller-minted signal on the subclass's aux output
        /// (<see cref="AuxOutputIndex"/>) instead of Success/Fail. Used for a third outcome
        /// such as the Reasoner's tool-call route. The caller mints the signal so it can carry
        /// structured content (e.g. tool-call blocks).
        /// </summary>
        /// <param name="signal">The pre-minted signal to latch and re-emit on the aux output.</param>
        /// <param name="message">An optional runtime message to surface.</param>
        /// <param name="level">The level for the runtime message.</param>
        /// <returns>An aux <see cref="RoutingResult"/>.</returns>
        public static RoutingResult Aux(PhySignal signal, string? message = null, GH_RuntimeMessageLevel level = GH_RuntimeMessageLevel.Blank) =>
            new(true, string.Empty, message, level, signal, null);

        /// <summary>
        /// Creates a result that latches the same caller-minted signal on <em>both</em> the
        /// Success and Fail outputs, so a terminal component routes one result identically down
        /// either branch. The caller mints the signal so it can carry structured content (e.g. a
        /// message plus an image).
        /// </summary>
        /// <param name="signal">The pre-minted signal to latch on both outputs.</param>
        /// <param name="message">An optional runtime message to surface.</param>
        /// <param name="level">The level for the runtime message.</param>
        /// <returns>A broadcast <see cref="RoutingResult"/>.</returns>
        public static RoutingResult Broadcast(PhySignal signal, string? message = null, GH_RuntimeMessageLevel level = GH_RuntimeMessageLevel.Blank) =>
            new(true, string.Empty, message, level, null, signal);
    }
}
