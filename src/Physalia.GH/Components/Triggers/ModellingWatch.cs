// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Recording;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// Watches the user model in Rhino and hands the procedure to the model, so it can be repeated. Tick
/// <b>Recording</b>, do the thing once by hand, untick it — and one signal carries the whole
/// demonstration.
///
/// <para><b>Rhino already knows what you did, which is why this is a small component.</b> The obvious
/// approach — diff the document against a previous state — is the hard way round: it needs a full
/// before-snapshot kept between rounds, a round trip per edit, and it recovers geometry rather than
/// intent. Rhino's own command events give the intent directly: <c>Offset</c>, <c>Fillet</c>,
/// <c>ExtrudeCrv</c>, in order, with the objects each one consumed and produced.</para>
///
/// <para><b>Fires when switched OFF, not per command</b> (<see cref="FiresOnDisarm"/>). A signal per
/// command would be a round per click, and there is no settle window that tells a pause for thought
/// apart from being finished — so the gesture that ends the demonstration is the one that hands it
/// over. Note the asymmetry with the harness panel's disarm-everything button, which switches this
/// off and deliberately sends NOTHING: somebody stopping every trigger is not asking for a round.</para>
///
/// <para><b>Direct edits are recorded too.</b> A gumball drag, a control point pulled, a nudge — none
/// of them is a command, and a recording that caught only the commands would teach a procedure with
/// the moves missing. Rhino's transform event carries the actual <see cref="Transform"/>, so a pure
/// translation is reported as the vector it was, which is the only part of a drag that can be
/// repeated.</para>
///
/// <para><b>The command line is captured because that is the only place the parameters exist.</b> The
/// command events carry the name and not the offset distance. Capture is fragile by nature — it is
/// text meant for a person — so the model gets it as supporting evidence alongside the before-and-after
/// counts, and is expected to reconcile the two rather than trust either.</para>
///
/// <para><b>What it does NOT solve.</b> Repeating a demonstration on geometry that DIFFERS is the
/// model generalising from one example, not a recording problem. Nothing here fixes that; what helps
/// is that every step reports its inputs as well as its outputs, and that the default closing
/// instruction makes the model describe the procedure back before applying it — while the inference
/// is still cheap to correct.</para>
/// </summary>
public class ModellingWatch : SignalSourceBase<ModellingEntry>
{
    private const int InCaptureCommandLine = 0;
    private const int InInstruction = 1;

    private static readonly int OutSteps = FirstAdditionalOutputIndex;
    private static readonly int OutStepCount = FirstAdditionalOutputIndex + 1;

    private bool _watching;

    private bool _captureCommandLine = true;

    // Whether Rhino's command-window capture was already on when we arrived, so switching it back is
    // a restore rather than a guess.
    private bool _captureWasEnabled;

    private string _instruction = string.Empty;

    // The command currently running, and what it has done so far. Null between commands.
    private string? _commandName;

    private DeltaBuilder? _commandDelta;

    // What has changed with NO command open — a gumball drag, a nudge. Flushed when a command starts
    // or the recording stops.
    private DeltaBuilder _directDelta = new();

    private string _steps = string.Empty;

    private int _stepCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModellingWatch"/> class.
    /// </summary>
    public ModellingWatch()
        : base(
            "Watch Modelling",
            "Watch",
            "Records what you do in Rhino and hands the procedure to the model so it can repeat it. Right-click and tick Recording, model the thing once, then untick it — that is what sends it.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F3A81C05-27B6-4E94-8D7A-401CB9E6725F");

    /// <inheritdoc/>
    /// <remarks>See the class remarks: a signal per command would be a round per click.</remarks>
    protected override bool FiresOnDisarm => true;

    /// <inheritdoc/>
    /// <remarks>
    /// "Recording" rather than "Armed", because switching it off is the act that produces the result
    /// — the reverse of every other trigger, where switching it on is.
    /// </remarks>
    protected override string ArmMenuText => "Recording";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "Fires once when you stop recording, carrying the whole procedure in order. Wire into a Conversation Log's Prompt Signal input.";

    /// <inheritdoc/>
    protected override string ArmedCaption =>
        PendingCount == 0
            ? "recording"
            : $"recording · {PendingCount.ToString(CultureInfo.InvariantCulture)}";

    /// <inheritdoc/>
    protected override void RegisterSourceInputs(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter(
            "Capture Command Line",
            "C",
            "Also record what Rhino printed to the command line, which is the only place a command's parameters exist as text — the distance, the radius, the options you typed. Leave it on unless something else in Rhino needs that buffer.",
            GH_ParamAccess.item,
            true);
        pManager.AddTextParameter(
            "Instruction",
            "I",
            "What to ask the model to do with the recording. Blank asks it to describe the procedure back and WAIT — which is what you want the first time, because it will have inferred some of the parameters and nobody has checked them yet.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[InInstruction].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Steps",
            "S",
            "The last recorded procedure as text, so it can be read on the canvas or written to a panel without going through the conversation.",
            GH_ParamAccess.item);
        pManager.AddIntegerParameter(
            "Step Count",
            "N",
            "How many steps survived the filtering. Wire into a comparison and a Signal Gate to stop a pipeline acting on a recording that caught nothing.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        bool capture = true;
        string instruction = string.Empty;
        da.GetData(InCaptureCommandLine, ref capture);
        da.GetData(InInstruction, ref instruction);

        _captureCommandLine = capture;
        _instruction = instruction ?? string.Empty;

        if (RhinoDoc.ActiveDoc is null)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "No Rhino document is open, so there is nothing to watch.");
        }
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        da.SetData(OutSteps, _steps);
        da.SetData(OutStepCount, _stepCount);
    }

    /// <inheritdoc/>
    protected override void StartListening()
    {
        if (_watching)
        {
            return;
        }

        _watching = true;
        _commandName = null;
        _commandDelta = null;
        _directDelta = new DeltaBuilder();

        Command.BeginCommand += OnBeginCommand;
        Command.EndCommand += OnEndCommand;
        Command.UndoRedo += OnUndoRedo;

        RhinoDoc.AddRhinoObject += OnObjectAdded;
        RhinoDoc.UndeleteRhinoObject += OnObjectAdded;
        RhinoDoc.DeleteRhinoObject += OnObjectDeleted;
        RhinoDoc.ReplaceRhinoObject += OnObjectReplaced;
        RhinoDoc.ModifyObjectAttributes += OnAttributesModified;
        // BEFORE, not After: the after-event carries only a transform id, while the before-event
        // carries the Transform itself, the object count and whether it is a dragged copy — which is
        // the whole reason a gumball drag can be recorded as something repeatable. Firing before the
        // change is applied is harmless here: what is being recorded is the intent, and a transform
        // inside a command the user then cancels is discarded with that command anyway.
        RhinoDoc.BeforeTransformObjects += OnObjectsTransformed;

        if (_captureCommandLine)
        {
            // Restored rather than switched off on the way out: something else in Rhino may have
            // wanted this on, and turning it off for them would be a quiet regression in their tool.
            _captureWasEnabled = RhinoApp.CommandWindowCaptureEnabled;
            RhinoApp.CommandWindowCaptureEnabled = true;
            RhinoApp.CapturedCommandWindowStrings(true);
        }
    }

    /// <inheritdoc/>
    protected override void StopListening()
    {
        if (!_watching)
        {
            return;
        }

        _watching = false;

        // Anything dragged since the last command is still a step, and this is the last chance to
        // record it — the batch is handed over on the same gesture that got us here.
        FlushDirectEdit();

        Command.BeginCommand -= OnBeginCommand;
        Command.EndCommand -= OnEndCommand;
        Command.UndoRedo -= OnUndoRedo;

        RhinoDoc.AddRhinoObject -= OnObjectAdded;
        RhinoDoc.UndeleteRhinoObject -= OnObjectAdded;
        RhinoDoc.DeleteRhinoObject -= OnObjectDeleted;
        RhinoDoc.ReplaceRhinoObject -= OnObjectReplaced;
        RhinoDoc.ModifyObjectAttributes -= OnAttributesModified;
        RhinoDoc.BeforeTransformObjects -= OnObjectsTransformed;

        if (_captureCommandLine && !_captureWasEnabled)
        {
            RhinoApp.CommandWindowCaptureEnabled = false;
        }

        _commandName = null;
        _commandDelta = null;
    }

    /// <inheritdoc/>
    protected override string ComposePayload(IReadOnlyList<ModellingEntry> events)
    {
        ModellingProcedure procedure = ModellingRecorder.Distil(events);

        _stepCount = procedure.Steps.Count;
        _steps = ModellingRecorder.Render(procedure, _instruction);

        return _steps;
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _steps = string.Empty;
        _stepCount = 0;
    }

    // ---- command spans -------------------------------------------------------------------------

    private void OnBeginCommand(object? sender, CommandEventArgs e)
    {
        // Whatever was dragged before this command belongs to its own step, and it happened first.
        FlushDirectEdit();

        _commandName = e.CommandEnglishName;
        _commandDelta = new DeltaBuilder();

        // The selection at this moment is the command's INPUT — which is what makes a step
        // repeatable, since "Offset" alone says nothing about what was offset. Selecting is not
        // recorded as an operation of its own for the same reason: it is how a command is set up.
        RecordSelection(_commandDelta);

        if (_captureCommandLine)
        {
            // Cleared so what is read at EndCommand is this command's output and not the last one's.
            RhinoApp.CapturedCommandWindowStrings(true);
        }
    }

    private void OnEndCommand(object? sender, CommandEventArgs e)
    {
        DeltaBuilder delta = _commandDelta ?? new DeltaBuilder();
        string name = _commandName ?? e.CommandEnglishName;

        _commandName = null;
        _commandDelta = null;

        // Every command is reported, including the ones a demonstration should ignore. The judgement
        // is Core's — see ModellingRecorder — so the rules are testable rather than spread across an
        // event handler, and so an Undo's own object events go with the step that gets dropped
        // instead of leaking into the next direct edit.
        ReportEvent(new CommandStep(
            name,
            Outcome(e.CommandResult),
            delta.Build(),
            _captureCommandLine ? ReadTranscript() : Array.Empty<string>()));
    }

    private void OnUndoRedo(object? sender, UndoRedoEventArgs e)
    {
        // Reported as a MARK, not a step: an Undo means the step before it never happened. The order
        // relative to the "Undo" command's own Begin/End does not matter — that command entry is
        // dropped either way, so it is never on the list the mark pops from.
        if (e.IsBeginUndo)
        {
            ReportEvent(new UndoMark());
        }
        else if (e.IsBeginRedo)
        {
            ReportEvent(new RedoMark());
        }
    }

    // ---- object events -------------------------------------------------------------------------

    private DeltaBuilder? Target()
    {
        if (!_watching)
        {
            return null;
        }

        if (PipelineRhinoWrites.InProgress)
        {
            // The model writing through run_rhino_script or baking geometry. It is already in the
            // conversation, and recording it would teach the model its own actions back.
            return null;
        }

        return _commandDelta ?? _directDelta;
    }

    private void OnObjectAdded(object? sender, RhinoObjectEventArgs e)
    {
        DeltaBuilder? delta = Target();
        if (delta is null)
        {
            return;
        }

        delta.Added++;
        Describe(e.TheObject, delta.AddedTypes, delta.Layers);
    }

    private void OnObjectDeleted(object? sender, RhinoObjectEventArgs e)
    {
        DeltaBuilder? delta = Target();
        if (delta is null)
        {
            return;
        }

        delta.Removed++;
    }

    private void OnObjectReplaced(object? sender, RhinoReplaceObjectEventArgs e)
    {
        DeltaBuilder? delta = Target();
        if (delta is null)
        {
            return;
        }

        delta.Replaced++;
        Describe(e.NewRhinoObject, delta.AddedTypes, delta.Layers);
    }

    private void OnAttributesModified(object? sender, RhinoModifyObjectAttributesEventArgs e)
    {
        DeltaBuilder? delta = Target();
        if (delta is null)
        {
            return;
        }

        // Counted as an edit: moving something onto another layer or changing its colour is a real
        // step of a real procedure, and it is often the whole point of one.
        delta.Replaced++;
        Describe(e.RhinoObject, delta.AddedTypes, delta.Layers);
    }

    private void OnObjectsTransformed(object? sender, RhinoTransformObjectsEventArgs e)
    {
        DeltaBuilder? delta = Target();
        if (delta is null)
        {
            return;
        }

        delta.Transformed += Math.Max(e.ObjectCount, e.Objects?.Length ?? 0);

        if (e.ObjectsWillBeCopied)
        {
            // A dragged copy is an addition as well as a move, and the count arrives here rather
            // than through AddRhinoObject in a way we can rely on.
            delta.CopiedInPlace = true;
        }

        delta.NoteTransform(Summarise(e.Transform));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private void FlushDirectEdit()
    {
        if (_directDelta.IsEmpty)
        {
            _directDelta = new DeltaBuilder();
            return;
        }

        // The selection is read now rather than at the start of the drag, because a drag has no
        // start we hear about — but it is the same objects, since dragging them is what selected
        // them.
        RecordSelection(_directDelta);

        ReportEvent(new DirectEditStep(_directDelta.Build()));
        _directDelta = new DeltaBuilder();
    }

    private static void RecordSelection(DeltaBuilder delta)
    {
        RhinoDoc? doc = RhinoDoc.ActiveDoc;
        if (doc is null)
        {
            return;
        }

        try
        {
            foreach (RhinoObject obj in doc.Objects.GetSelectedObjects(includeLights: false, includeGrips: false))
            {
                delta.InputCount++;
                delta.InputTypes.Add(obj.ObjectType.ToString());
            }
        }
        catch (Exception)
        {
            // The inputs are a nicety; never let reading them cost the step.
        }
    }

    private static void Describe(RhinoObject? obj, HashSet<string> types, HashSet<string> layers)
    {
        if (obj is null)
        {
            return;
        }

        try
        {
            types.Add(obj.ObjectType.ToString());

            RhinoDoc? doc = RhinoDoc.ActiveDoc;
            if (doc is not null)
            {
                Layer layer = doc.Layers[obj.Attributes.LayerIndex];
                if (layer is { IsDeleted: false })
                {
                    layers.Add(layer.FullPath);
                }
            }
        }
        catch (Exception)
        {
            // A type or a layer we could not read costs a word of the description and nothing else.
        }
    }

    private string[] ReadTranscript()
    {
        try
        {
            // Cleared as it is read, so the next command starts from nothing.
            return RhinoApp.CapturedCommandWindowStrings(true) ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static CommandOutcome Outcome(Result result) => result switch
    {
        Result.Success => CommandOutcome.Success,

        // Nothing and Cancel are both "the user changed their mind", which is exactly what a
        // demonstration must not teach.
        Result.Cancel or Result.Nothing or Result.CancelModelessDialog => CommandOutcome.Cancelled,

        _ => CommandOutcome.Failed,
    };

    // A pure translation is reported as the vector it was, because that is the only part of a drag
    // that can be repeated. Anything else is left unnamed rather than guessed at: describing a
    // rotation-plus-scale in words the model would then act on is worse than saying "moved".
    private static string? Summarise(Transform transform)
    {
        if (!IsTranslation(transform))
        {
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "moved by {0:0.###}, {1:0.###}, {2:0.###}",
            transform.M03,
            transform.M13,
            transform.M23);
    }

    private static bool IsTranslation(Transform t) =>
        Near(t.M00, 1) && Near(t.M11, 1) && Near(t.M22, 1) && Near(t.M33, 1)
        && Near(t.M01, 0) && Near(t.M02, 0)
        && Near(t.M10, 0) && Near(t.M12, 0)
        && Near(t.M20, 0) && Near(t.M21, 0)
        && Near(t.M30, 0) && Near(t.M31, 0) && Near(t.M32, 0);

    private static bool Near(double value, double target) => Math.Abs(value - target) < 1e-9;

    // Accumulates one operation's effect. Mutable on purpose: it is filled from event handlers and
    // built into the immutable Core record once, when the operation ends.
    private sealed class DeltaBuilder
    {
        internal int InputCount { get; set; }

        internal HashSet<string> InputTypes { get; } = new(StringComparer.Ordinal);

        internal int Added { get; set; }

        internal int Removed { get; set; }

        internal int Replaced { get; set; }

        internal int Transformed { get; set; }

        internal bool CopiedInPlace { get; set; }

        internal HashSet<string> AddedTypes { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> Layers { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal bool IsEmpty =>
            Added == 0 && Removed == 0 && Replaced == 0 && Transformed == 0;

        private string? Transform { get; set; }

        private bool TransformConflict { get; set; }

        internal void NoteTransform(string? summary)
        {
            if (summary is null)
            {
                TransformConflict = true;
                return;
            }

            if (Transform is null)
            {
                Transform = summary;
                return;
            }

            if (!string.Equals(Transform, summary, StringComparison.Ordinal))
            {
                // Two different transforms in one operation. Reporting one of them would be a
                // parameter the model acts on and nobody meant.
                TransformConflict = true;
            }
        }

        internal ObjectDelta Build() => new(
            InputCount,
            Rank(InputTypes),
            Added,
            Removed,
            Replaced,
            Transformed,
            Rank(AddedTypes),
            Layers.ToList(),
            TransformConflict
                ? null
                : CopiedInPlace && Transform is { Length: > 0 }
                    ? Transform + " (as a copy)"
                    : Transform);

        private static IReadOnlyList<string> Rank(HashSet<string> types) =>
            types.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }
}
