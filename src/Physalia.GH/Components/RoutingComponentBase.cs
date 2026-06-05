// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.Common;

namespace Physalia.GH.Components;

/// <summary>
/// Base for components that route a result either forward (Data +
/// Success Trigger) or back as Feedback (Feedback + Fail Trigger). Owns all I/O
/// registration, output latching, momentary trigger pulses, the Clear Outputs
/// menu item, and save/load serialisation. Subclasses supply only the Data input
/// type and the per-component processing logic.
/// </summary>
/// <typeparam name="TData">Type produced from the Data input and handed to Process.</typeparam>
public abstract class RoutingComponentBase<TData> : PhyBase
{
    /// <summary>Output index of the latched Data string.</summary>
    protected const int OutData = 0;

    /// <summary>Output index of the Success Trigger pulse.</summary>
    protected const int OutSuccessTrigger = 1;

    /// <summary>Output index of the latched Feedback string.</summary>
    protected const int OutFeedback = 2;

    /// <summary>Output index of the Fail Trigger pulse.</summary>
    protected const int OutFailTrigger = 3;

    private string _dataOut = string.Empty;
    private string _feedbackOut = string.Empty;
    private string? _lastSignature;
    private bool _fireSuccess;
    private bool _fireFail;

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
    /// Reads the Data input. Returns false when the input is empty, which drives
    /// the clear-on-empty behaviour in the base.
    /// </summary>
    /// <param name="da">The data access for the current solve.</param>
    /// <param name="data">The parsed Data value when present.</param>
    /// <returns>true if Data is present and non-empty; otherwise false.</returns>
    protected abstract bool TryGetData(IGH_DataAccess da, out TData data);

    /// <summary>
    /// Performs the per-component work when the inputs change. Read any additional
    /// inputs directly from <paramref name="da"/>.
    /// </summary>
    /// <param name="data">The Data value read by <see cref="TryGetData"/>.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>A success result carrying the Data string, or a failure result carrying Feedback.</returns>
    protected abstract RoutingResult Process(TData data, IGH_DataAccess da);

    /// <inheritdoc/>
    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        RegisterDataInput(pManager);
        RegisterAdditionalInputs(pManager);
    }

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Data", "D", "Latched result on success. Persists until the next run or a clear.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Success Trigger", "ST", "Pulses true for one solve when the run succeeds.", GH_ParamAccess.item);
        pManager.AddTextParameter("Feedback", "F", "Latched feedback on failure. Persists until the next run or a clear.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Fail Trigger", "FT", "Pulses true for one solve when the run fails.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        string signature = ComputeInputSignature();

        if (!TryGetData(DA, out TData data))
        {
            // Clear-on-empty: an empty Data input wipes the latched outputs.
            _dataOut = string.Empty;
            _feedbackOut = string.Empty;
            Emit(DA, success: false, fail: false);
            _lastSignature = signature;
            return;
        }

        bool changed = !string.Equals(signature, _lastSignature, StringComparison.Ordinal);
        _lastSignature = signature;

        if (changed)
        {
            RoutingResult result = Process(data, DA);

            if (result.Message != null)
            {
                AddRuntimeMessage(result.MessageLevel, result.Message);
            }

            if (result.Success)
            {
                _dataOut = result.Output;
                _feedbackOut = string.Empty;
                _fireSuccess = true;
                _fireFail = false;
            }
            else
            {
                _dataOut = string.Empty;
                _feedbackOut = result.Output;
                _fireFail = true;
                _fireSuccess = false;
            }

            ScheduleTriggerReset();
        }

        Emit(DA, _fireSuccess, _fireFail);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Clear Outputs", (_, _) => ClearOutputs());
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

        if (_lastSignature != null)
        {
            writer.SetString("LastSignature", _lastSignature);
        }

        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _dataOut = reader.ItemExists("DataOut") ? reader.GetString("DataOut") : string.Empty;
        _feedbackOut = reader.ItemExists("FeedbackOut") ? reader.GetString("FeedbackOut") : string.Empty;
        _lastSignature = reader.ItemExists("LastSignature") ? reader.GetString("LastSignature") : null;
        return base.Read(reader);
    }

    private void Emit(IGH_DataAccess da, bool success, bool fail)
    {
        da.SetData(OutData, _dataOut);
        da.SetData(OutSuccessTrigger, success);
        da.SetData(OutFeedback, _feedbackOut);
        da.SetData(OutFailTrigger, fail);
    }

    private void ScheduleTriggerReset()
    {
        OnPingDocument()?.ScheduleSolution(1, _ =>
        {
            _fireSuccess = false;
            _fireFail = false;
            ExpireSolution(true);
        });
    }

    /// <summary>
    /// Builds a stable hash over every input's volatile data. A change in the hash
    /// between solves means an input changed and the component should re-run; an
    /// unchanged hash (the pulse-reset re-solve, a canvas move, file reload) skips
    /// re-processing so the latched outputs simply re-emit.
    /// </summary>
    /// <returns>A hex-encoded SHA-256 hash of the current input set.</returns>
    private string ComputeInputSignature()
    {
        var sb = new StringBuilder();
        foreach (IGH_Param param in Params.Input)
        {
            sb.Append(param.Name).Append('');
            foreach (IGH_Goo goo in param.VolatileData.AllData(true))
            {
                sb.Append(goo?.ToString() ?? "∅").Append('');
            }

            sb.Append('');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private void ClearOutputs()
    {
        _dataOut = string.Empty;
        _feedbackOut = string.Empty;
        _fireSuccess = false;
        _fireFail = false;
        ExpireSolution(true);
    }

    /// <summary>
    /// Outcome of a <see cref="Process"/> call: either a forward-routed Data string
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
