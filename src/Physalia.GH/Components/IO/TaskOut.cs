// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Where a delegated task ANSWERS. The end of a callable harness: whatever signal reaches this node
/// is what the Delegate tool in the calling pipeline gets back, and the calling model sees it as the
/// result of its own tool call.
///
/// <para><b>An endpoint, not a passthrough</b> — no outputs, like Harness Out. What arrives here
/// leaves the harness, and there is nothing further inside for it to reach.</para>
///
/// <para><b>It answers with the whole signal, not only its text.</b> The payload is the answer, and
/// the content blocks travel with it — so a sub-pipeline whose job was to LOOK at something can hand
/// back the snapshot it took, and the calling model sees the image. That rides the machinery a tool
/// already has for answering with something that is not text: a tool result is text on every
/// provider, so an image goes back as an attachment on the same turn.</para>
///
/// <para><b>Reaching it with nobody waiting is not an error.</b> A callable harness is still an
/// ordinary pipeline — someone will open its chat window and run it by hand while building it — and
/// the answer simply has nowhere to go. It says so as a remark, because the alternative is a
/// permanent warning on a node that is working correctly.</para>
/// </summary>
public class TaskOut : StatefulComponentBase
{
    private const int InSignal = 0;

    private int _answered;

    private bool _orphaned;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOut"/> class.
    /// </summary>
    public TaskOut()
        : base(
            "Task Out",
            "TaskOut",
            "The end of a callable harness: what reaches here is what the Delegate tool that called it gets back. Images the signal carries go back too.",
            "I/O")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7F26B4A1-90D3-4C85-A6E7-13B08542FD6C");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_Signal(),
            "Signal",
            "S",
            "The finished answer. Its payload is what the calling model reads, and any images it carries go back with it.",
            GH_ParamAccess.list);
        pManager[InSignal].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No outputs: the answer leaves the harness here. Same shape as Harness Out.
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        ObserveSignalInputs(DA, InSignal);

        foreach (ConsumedSignal item in ConsumeAllSignals(InSignal))
        {
            bool taken = DelegationBroker.Complete(
                OnPingDocument(),
                item.Signal.Payload,
                item.Signal.ContentBlocks);

            _orphaned = !taken;

            if (taken)
            {
                _answered++;
            }

            // Quiet: there is nothing downstream of an endpoint, and minting a signal nobody can
            // consume would show up in the trace as an event that went nowhere.
            LatchSuccess(item.Signal.Payload, emitSignal: false);
        }

        if (_orphaned)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Remark,
                "Nothing was waiting for this answer — no Delegate tool had called this harness. That is normal while building the pipeline by hand.");
        }
    }

    /// <inheritdoc/>
    protected override string MessageForState(SolveState state) =>
        _answered == 0
            ? (_orphaned ? "nobody waiting" : string.Empty)
            : $"{_answered} answered";

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        _answered = 0;
        _orphaned = false;
    }
}
