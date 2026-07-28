// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Planning;
using Physalia.Core.Signals;

namespace Physalia.GH.Components;

/// <summary>
/// Reads the staged build plan out of each model response and renders the progress digest that
/// keeps an incremental build going. A tap, not a gate: the response always passes through
/// unchanged on the single Signal output, so wiring this in can never cost a submission.
///
/// <para>Incremental building — place a slice, measure it, place the next — needs one thing the
/// rest of the pipeline cannot supply: an answer to "am I done?". Every guardrail downstream
/// reports on the graph that exists, and a clean report on a correct first slice is
/// indistinguishable from a clean report on a finished definition. The model is therefore offered
/// an exit at stage one, and takes it. The plan is the missing anchor, and it has to come from the
/// model because only the model knows what the request decomposes into: it declares the stages up
/// front in a plain-text block ahead of its JSON, restates the block each turn with the stage it
/// is building now, and this component reads it back as facts — stages built, stage just placed,
/// stages outstanding — that no report can contradict.</para>
///
/// <para>Wire the Progress output into the Geometry Report's Message input. The report folds it in
/// as its operator note and, recognising the digest, defers its own "reply in prose if this
/// matches your intent" closing line to the digest's staged instruction — which says continue
/// while stages remain and only invites prose on the last one. Wire the Signal in from the Detect
/// JSON gate and out to the Schema Validator, so the tracker sees the raw response with the plan
/// block still on it (the validators strip everything but the JSON) and never sees the closing
/// prose turn that ends the build.</para>
/// </summary>
public class BuildPlanTracker : RoutingComponentBase<string>
{
    // The plan as last declared, and the stage the model says it is building. Session-only, like
    // every other lifecycle state: a reopened document starts with no plan and picks one up from
    // the next response.
    private BuildPlan? _plan;
    private int _stage;
    private string _progress = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildPlanTracker"/> class.
    /// </summary>
    public BuildPlanTracker()
        : base(
            "Build Plan",
            "Plan",
            "Reads the model's staged build plan out of each response and renders a progress digest (stages built, stage just placed, stages outstanding) for the Geometry Report's Message input. The response passes through unchanged.",
            "Control Flow")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3F7A61C4-52D8-4E19-9B0A-C6E4D28F5A73");

    /// <inheritdoc/>
    /// <remarks>
    /// A single Signal output: the tracker never rejects anything, so a Fail output would have
    /// nothing to carry. A response with no plan block passes through exactly like one with.
    /// </remarks>
    protected override bool HasFailOutput => false;

    // The Progress text output, after the single Signal output.
    private int ProgressOutputIndex => FirstAdditionalOutputIndex;

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Progress",
            "P",
            "The build-progress digest for the stage just submitted: the plan read back with each stage marked built, current, or outstanding, and the instruction that decides whether the loop continues. Wire into the Geometry Report's Message input. Empty until a response declares a plan.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Re-published every solve, including idle ones, so the digest stays on the wire for the
    /// Geometry Report's later read pass rather than blanking between solves.
    /// </remarks>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        da.SetData(ProgressOutputIndex, _progress);
    }

    /// <inheritdoc/>
    /// <remarks>The raw response — plan block and JSON document together — arrives as the payload.</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — parsing is pure string work, all of it in ReadSolve.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: reading a plan has no side effects to settle.
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        if (BuildPlanParser.Parse(data) is { } parsed)
        {
            _plan = parsed;

            // A response that omits "now:" leaves the stage where it was. Advancing on a guess
            // would mark an unbuilt stage as built on the very round where that matters most —
            // a correction round rebuilds the stage it just failed, and says so by restating it.
            if (parsed.CurrentStage > 0)
            {
                _stage = parsed.CurrentStage;
            }
            else if (_stage == 0)
            {
                _stage = parsed.Stages[0].Number;
            }
        }

        if (_plan is null)
        {
            _progress = string.Empty;
            return RoutingResult.Ok(
                data,
                message: "No <plan> block in this response, so no progress digest was produced — the build runs unstaged.",
                level: GH_RuntimeMessageLevel.Remark);
        }

        _progress = BuildPlanParser.RenderProgress(_plan, _stage);
        return RoutingResult.Ok(
            data,
            message: $"Stage {_stage} of {LastStageNumber}: {StageDescription(_stage)}",
            level: GH_RuntimeMessageLevel.Remark);
    }

    /// <inheritdoc/>
    /// <remarks>The canvas caption carries the stage, so a glance at the node says how far along the build is.</remarks>
    protected override string MessageForState(SolveState state) =>
        state == SolveState.SolveSuccess && _plan is not null
            ? $"stage {_stage} / {LastStageNumber}"
            : base.MessageForState(state);

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _plan = null;
        _stage = 0;
        _progress = string.Empty;
    }

    // The highest stage number the plan declares — the plan's length as the model numbered it,
    // which is not the stage count when the numbering skips.
    private int LastStageNumber => _plan is null ? 0 : _plan.Stages[^1].Number;

    private string StageDescription(int stage)
    {
        foreach (BuildStage item in _plan!.Stages)
        {
            if (item.Number == stage)
            {
                return item.Description;
            }
        }

        return "(not in the declared plan)";
    }
}
