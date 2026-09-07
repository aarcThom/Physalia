// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Recording;
using Xunit;

namespace Physalia.Core.Tests.Recording;

public class ModellingRecorderTests
{
    private static ObjectDelta Made(int added = 1, string type = "Curve", string? layer = null) =>
        new(
            0,
            Array.Empty<string>(),
            added,
            0,
            0,
            0,
            new[] { type },
            layer is null ? Array.Empty<string>() : new[] { layer },
            null);

    private static ObjectDelta Moved(int count = 1, string? how = null) =>
        new(count, new[] { "Brep" }, 0, 0, 0, count, Array.Empty<string>(), Array.Empty<string>(), how);

    private static CommandStep Cmd(
        string name,
        ObjectDelta? delta = null,
        CommandOutcome outcome = CommandOutcome.Success,
        params string[] transcript) =>
        new(name, outcome, delta ?? Made(), transcript);

    // ---- what survives -------------------------------------------------------------------------

    [Fact]
    public void ACommandThatChangedTheDocumentIsKept()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new[] { Cmd("Offset") });

        ProcedureStep step = Assert.Single(result.Steps);
        Assert.Equal(1, step.Number);
        Assert.Equal("Offset", step.Name);
    }

    [Fact]
    public void ACommandThatChangedNothingIsDiscarded_NotKeptAsANoOp()
    {
        // A Trim that found no intersection. It ran, it succeeded, and there is nothing to repeat.
        ModellingProcedure result = ModellingRecorder.Distil(new[] { Cmd("Trim", ObjectDelta.Nothing) });

        Assert.Empty(result.Steps);
        Assert.Equal(1, result.Discarded);
    }

    [Theory]
    [InlineData(CommandOutcome.Cancelled)]
    [InlineData(CommandOutcome.Failed)]
    public void ACancelledOrFailedCommandIsDiscarded(CommandOutcome outcome)
    {
        ModellingProcedure result = ModellingRecorder.Distil(new[] { Cmd("Fillet", Made(), outcome) });

        Assert.Empty(result.Steps);
        Assert.Equal(1, result.Discarded);
    }

    [Fact]
    public void SelectingAndNavigatingNeverReachTheProcedure_BecauseTheyChangeNothing()
    {
        // Judged by EFFECT, not by name: this is why the name list does not have to be a Rhino
        // command index.
        ModellingProcedure result = ModellingRecorder.Distil(new[]
        {
            Cmd("Zoom", ObjectDelta.Nothing),
            Cmd("SelBrush", ObjectDelta.Nothing),
            Cmd("Pan", ObjectDelta.Nothing),
            Cmd("ExtrudeCrv"),
        });

        Assert.Equal("ExtrudeCrv", Assert.Single(result.Steps).Name);
    }

    [Fact]
    public void ASaveIsLeftOutEvenIfSomethingChanged()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new[] { Cmd("Save", Made()) });

        Assert.Empty(result.Steps);

        // Not counted as discarded either: it was never a candidate, so counting it would make the
        // "N discarded" figure meaningless.
        Assert.Equal(0, result.Discarded);
    }

    [Fact]
    public void GrasshopperAndScriptingAreLeftOut_BecauseThatIsThePipelineActing()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new[]
        {
            Cmd("RunPythonScript", Made(added: 40)),
            Cmd("Grasshopper", Made()),
            Cmd("Loft"),
        });

        Assert.Equal("Loft", Assert.Single(result.Steps).Name);
    }

    [Theory]
    [InlineData("_Offset")]
    [InlineData("-Offset")]
    public void AScriptedOrCommandLineFormIsTheSameCommand(string name)
    {
        Assert.Equal("Offset", Assert.Single(ModellingRecorder.Distil(new[] { Cmd(name) }).Steps).Name);
    }

    [Fact]
    public void ScriptedFormsOfIgnoredCommandsAreStillIgnored()
    {
        Assert.True(ModellingRecorder.IsNotModelling("_Undo"));
        Assert.True(ModellingRecorder.IsNotModelling("-Save"));
        Assert.True(ModellingRecorder.IsNotModelling(null));
        Assert.False(ModellingRecorder.IsNotModelling("Offset"));
    }

    // ---- undo ---------------------------------------------------------------------------------

    [Fact]
    public void AnUndoRemovesTheStepBeforeIt_RatherThanBeingRecordedAsAStep()
    {
        // The rule that matters most: an Undo means the step never happened, and a demonstration
        // containing both is a demonstration of nothing.
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            Cmd("Offset"),
            Cmd("Fillet"),
            new UndoMark(),
        });

        Assert.Equal("Offset", Assert.Single(result.Steps).Name);
        Assert.Equal(1, result.Undone);
    }

    [Fact]
    public void SeveralUndosUnwindSeveralSteps()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            Cmd("Line"),
            Cmd("Offset"),
            Cmd("Fillet"),
            new UndoMark(),
            new UndoMark(),
        });

        Assert.Equal("Line", Assert.Single(result.Steps).Name);
        Assert.Equal(2, result.Undone);
    }

    [Fact]
    public void RedoPutsTheStepBack()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            Cmd("Offset"),
            new UndoMark(),
            new RedoMark(),
        });

        Assert.Equal("Offset", Assert.Single(result.Steps).Name);
        Assert.Equal(0, result.Undone);
    }

    [Fact]
    public void AnUndoWithNothingToUndoIsIgnored()
    {
        // Undoing past the start of the recording. There is nothing of ours to remove, and the count
        // must not claim otherwise.
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            new UndoMark(),
            Cmd("Offset"),
        });

        Assert.Single(result.Steps);
        Assert.Equal(0, result.Undone);
    }

    [Fact]
    public void ARedoWithNothingToRedoIsIgnored()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            new RedoMark(),
            Cmd("Offset"),
        });

        Assert.Single(result.Steps);
    }

    [Fact]
    public void UndoSkipsOverDiscardedCommands_SoItRemovesARealStep()
    {
        // A cancelled Fillet between the Offset and the Undo. The Undo undid the OFFSET, because the
        // cancelled command left nothing on the undo stack either.
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            Cmd("Offset"),
            Cmd("Fillet", Made(), CommandOutcome.Cancelled),
            new UndoMark(),
        });

        Assert.Empty(result.Steps);
        Assert.Equal(1, result.Undone);
        Assert.Equal(1, result.Discarded);
    }

    // ---- direct edits -------------------------------------------------------------------------

    [Fact]
    public void AGumballDragIsAStep()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            new DirectEditStep(Moved(2, "moved by 0, 0, 1200")),
        });

        ProcedureStep step = Assert.Single(result.Steps);
        Assert.Equal(ModellingRecorder.DirectEditName, step.Name);
        Assert.Equal("moved by 0, 0, 1200", step.Delta.TransformSummary);
    }

    [Fact]
    public void ADirectEditThatChangedNothingIsNotAStep()
    {
        Assert.Empty(ModellingRecorder.Distil(new ModellingEntry[] { new DirectEditStep(ObjectDelta.Nothing) }).Steps);
    }

    [Fact]
    public void AnUndoCanRemoveADirectEdit()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            new DirectEditStep(Moved()),
            new UndoMark(),
        });

        Assert.Empty(result.Steps);
    }

    // ---- folding ------------------------------------------------------------------------------

    [Fact]
    public void ConsecutiveRunsOfOneCommandFoldIntoOneStep()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new[]
        {
            Cmd("Offset"),
            Cmd("Offset"),
            Cmd("Offset"),
        });

        ProcedureStep step = Assert.Single(result.Steps);
        Assert.Equal(3, step.Repeats);
        Assert.Equal(3, step.Delta.Added);
    }

    [Fact]
    public void FoldingIsCONSECUTIVEOnly_SoTheShapeOfTheProcedureSurvives()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new[]
        {
            Cmd("Offset"),
            Cmd("Trim"),
            Cmd("Offset"),
        });

        Assert.Equal(new[] { "Offset", "Trim", "Offset" }, result.Steps.Select(s => s.Name));
        Assert.Equal(new[] { 1, 2, 3 }, result.Steps.Select(s => s.Number));
    }

    [Fact]
    public void FoldingDropsATransformSummaryThatDisagrees()
    {
        // Two drags by different amounts. Reporting one of them as though it were both would be
        // worse than reporting neither.
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            new DirectEditStep(Moved(1, "moved by 0, 0, 100")),
            new DirectEditStep(Moved(1, "moved by 0, 0, 900")),
        });

        ProcedureStep step = Assert.Single(result.Steps);
        Assert.Equal(2, step.Repeats);
        Assert.Null(step.Delta.TransformSummary);
    }

    [Fact]
    public void FoldingKeepsATransformSummaryThatAgrees()
    {
        ModellingProcedure result = ModellingRecorder.Distil(new ModellingEntry[]
        {
            new DirectEditStep(Moved(1, "moved by 0, 0, 100")),
            new DirectEditStep(Moved(1, "moved by 0, 0, 100")),
        });

        Assert.Equal("moved by 0, 0, 100", Assert.Single(result.Steps).Delta.TransformSummary);
    }

    // ---- rendering ----------------------------------------------------------------------------

    [Fact]
    public void TheSelectionIsReportedAsSELECTED_NotAsAnInput()
    {
        // Rhino never says which objects a command consumed, only what was highlighted when it
        // started — which for a creation command is leftover selection. Calling it an input claimed
        // a circle was made FROM the three things that happened to be selected.
        string text = ModellingRecorder.Render(ModellingRecorder.Distil(new[]
        {
            Cmd("Circle", new ObjectDelta(
                3, new[] { "Curve" }, 1, 0, 0, 0, new[] { "Curve" }, Array.Empty<string>(), null)),
        }));

        Assert.Contains("3 Curve selected", text);
        Assert.DoesNotContain("3 Curve in", text);
    }

    [Fact]
    public void RenderNumbersTheStepsAndSaysWhatEachDid()
    {
        string text = ModellingRecorder.Render(ModellingRecorder.Distil(new[]
        {
            Cmd("Offset", Made(3, "Curve", "Slab edge")),
            Cmd("ExtrudeCrv", Made(3, "Brep")),
        }));

        Assert.Contains("2 steps", text);
        Assert.Contains("1. Offset", text);
        Assert.Contains("3 Curve added", text);
        Assert.Contains("\"Slab edge\"", text);
        Assert.Contains("2. ExtrudeCrv", text);
    }

    [Fact]
    public void RenderReportsWhatWasThrownAway()
    {
        // "6 steps, 4 discarded" is the difference between a filter and a bug, to a reader.
        string text = ModellingRecorder.Render(ModellingRecorder.Distil(new ModellingEntry[]
        {
            Cmd("Offset"),
            Cmd("Fillet", Made(), CommandOutcome.Cancelled),
            Cmd("Loft"),
            new UndoMark(),
        }));

        Assert.Contains("cancelled or ineffective", text);
        Assert.Contains("undone by the user", text);
    }

    [Fact]
    public void RenderCarriesTheCommandLineText_BecauseThatIsWhereTheParametersAre()
    {
        string text = ModellingRecorder.Render(ModellingRecorder.Distil(new[]
        {
            Cmd("Offset", Made(), CommandOutcome.Success, "Offset  Distance=200  Corner=Sharp"),
        }));

        Assert.Contains("Distance=200", text);
    }

    [Fact]
    public void TranscriptLinesAreCapped()
    {
        string[] many = Enumerable.Range(0, 40).Select(i => $"line{i}").ToArray();

        ProcedureStep step = Assert.Single(
            ModellingRecorder.Distil(new[] { Cmd("Offset", Made(), CommandOutcome.Success, many) }).Steps);

        Assert.Equal(ModellingRecorder.MaxTranscriptLines, step.Transcript.Count);
    }

    [Fact]
    public void RenderEndsByAskingTheModelToWait()
    {
        // A demonstration arrives as a user turn, and the natural next thing for a model to do is
        // start applying it — with parameters it inferred and nobody checked.
        string text = ModellingRecorder.Render(ModellingRecorder.Distil(new[] { Cmd("Offset") }));

        Assert.Contains("Do not apply it to anything yet", text);
    }

    [Fact]
    public void TheClosingInstructionCanBeOverridden()
    {
        string text = ModellingRecorder.Render(
            ModellingRecorder.Distil(new[] { Cmd("Offset") }),
            "Apply it to the selected blocks.");

        Assert.Contains("Apply it to the selected blocks.", text);
        Assert.DoesNotContain("Do not apply it", text);
    }

    [Fact]
    public void AnEmptyRecordingSaysSoRatherThanBeingBlank()
    {
        // Usually means the watch was armed after the work. A person needs telling.
        string text = ModellingRecorder.Render(ModellingRecorder.Distil(Array.Empty<ModellingEntry>()));

        Assert.Contains("Nothing was recorded", text);
    }

    [Fact]
    public void AnEmptyRecordingMentionsCommandsItSawButDropped()
    {
        string text = ModellingRecorder.Render(ModellingRecorder.Distil(new[]
        {
            Cmd("Fillet", Made(), CommandOutcome.Cancelled),
            Cmd("Fillet", Made(), CommandOutcome.Cancelled),
        }));

        Assert.Contains("Nothing was recorded", text);
        Assert.Contains("2 commands were seen", text);
    }

    [Fact]
    public void NullsAreTolerated()
    {
        Assert.Empty(ModellingRecorder.Distil(null).Steps);
        Assert.Contains("Nothing was recorded", ModellingRecorder.Render(null));
    }
}
