// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Physalia.Core.Recording;

/// <summary>
/// How a Rhino command ended.
/// </summary>
public enum CommandOutcome
{
    /// <summary>It did what it was asked.</summary>
    Success,

    /// <summary>The user pressed Escape, or answered nothing.</summary>
    Cancelled,

    /// <summary>It ran and failed.</summary>
    Failed,
}

/// <summary>
/// What one operation did to the document.
/// </summary>
/// <param name="InputCount">How many objects were selected when it started.</param>
/// <param name="InputTypes">The kinds of those objects, most common first.</param>
/// <param name="Added">Objects created.</param>
/// <param name="Removed">Objects deleted.</param>
/// <param name="Replaced">Objects edited in place.</param>
/// <param name="Transformed">Objects moved, rotated or scaled.</param>
/// <param name="AddedTypes">The kinds of the created objects.</param>
/// <param name="Layers">The layers touched.</param>
/// <param name="TransformSummary">
/// A description of the transform, when there was exactly one and it is worth stating — a gumball
/// drag is a whole modelling step and "moved by 0, 0, 1200" is the only part of it that can be
/// repeated. Null when there was no transform or several different ones.
/// </param>
public sealed record ObjectDelta(
    int InputCount,
    IReadOnlyList<string> InputTypes,
    int Added,
    int Removed,
    int Replaced,
    int Transformed,
    IReadOnlyList<string> AddedTypes,
    IReadOnlyList<string> Layers,
    string? TransformSummary)
{
    /// <summary>Gets a delta that changed nothing.</summary>
    public static ObjectDelta Nothing { get; } = new(
        0,
        Array.Empty<string>(),
        0,
        0,
        0,
        0,
        Array.Empty<string>(),
        Array.Empty<string>(),
        null);

    /// <summary>
    /// Gets a value indicating whether the document was left as it was found.
    ///
    /// <para>A selection on its own does not count: selecting things is how you set a command up, not
    /// something a demonstration can repeat.</para>
    /// </summary>
    public bool IsEmpty => Added == 0 && Removed == 0 && Replaced == 0 && Transformed == 0;

    /// <summary>
    /// Adds another delta to this one, for two runs of the same command folded into one step.
    /// </summary>
    /// <param name="other">The delta to fold in.</param>
    /// <returns>The combined delta.</returns>
    public ObjectDelta Plus(ObjectDelta other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ObjectDelta(
            InputCount + other.InputCount,
            Merge(InputTypes, other.InputTypes),
            Added + other.Added,
            Removed + other.Removed,
            Replaced + other.Replaced,
            Transformed + other.Transformed,
            Merge(AddedTypes, other.AddedTypes),
            Merge(Layers, other.Layers),

            // Dropped rather than picked from: two folded runs moved things by two different amounts,
            // and reporting one of them as though it were both is worse than reporting neither.
            TransformSummary is not null && TransformSummary == other.TransformSummary
                ? TransformSummary
                : null);
    }

    private static IReadOnlyList<string> Merge(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Concat(b).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>
/// One thing observed while watching somebody model. The raw stream, before any judgement.
/// </summary>
public abstract record ModellingEntry;

/// <summary>
/// A Rhino command that ran.
/// </summary>
/// <param name="Name">Its English name.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Delta">What it did to the document.</param>
/// <param name="Transcript">
/// The command-line text it produced, which is the ONLY place its parameters exist as text — the
/// command events carry the name and not the offset distance.
/// </param>
public sealed record CommandStep(
    string Name,
    CommandOutcome Outcome,
    ObjectDelta Delta,
    IReadOnlyList<string> Transcript) : ModellingEntry;

/// <summary>
/// A change made without a command — a gumball drag, a control point pulled, a nudge.
///
/// <para>Too common to leave out. A real modelling session is full of them, and a recording that only
/// caught the commands would teach a procedure with the moves missing.</para>
/// </summary>
/// <param name="Delta">What changed.</param>
public sealed record DirectEditStep(ObjectDelta Delta) : ModellingEntry;

/// <summary>
/// The user pressed Undo. Not a step: it says the step before it did not happen.
/// </summary>
public sealed record UndoMark : ModellingEntry;

/// <summary>
/// The user pressed Redo, putting back what an Undo took away.
/// </summary>
public sealed record RedoMark : ModellingEntry;

/// <summary>
/// One step of the distilled procedure.
/// </summary>
/// <param name="Number">Its place in the procedure, from 1.</param>
/// <param name="Name">The command, or "Direct edit".</param>
/// <param name="Repeats">How many consecutive runs were folded into this step.</param>
/// <param name="Delta">What it did, summed across those runs.</param>
/// <param name="Transcript">The command-line text, capped.</param>
public sealed record ProcedureStep(
    int Number,
    string Name,
    int Repeats,
    ObjectDelta Delta,
    IReadOnlyList<string> Transcript);

/// <summary>
/// What somebody actually demonstrated.
/// </summary>
/// <param name="Steps">The surviving steps, in order.</param>
/// <param name="Discarded">Commands dropped as cancelled, ineffective or irrelevant.</param>
/// <param name="Undone">Steps removed because the user undid them.</param>
public sealed record ModellingProcedure(
    IReadOnlyList<ProcedureStep> Steps,
    int Discarded,
    int Undone);

/// <summary>
/// Turns a raw stream of observed Rhino activity into the procedure somebody meant to demonstrate.
///
/// <para><b>The filtering IS the feature.</b> A real modelling session is half navigation and half
/// false starts: pans and zooms, a selection built up over four clicks, a Move that was immediately
/// undone, a Fillet that was cancelled because the radius was wrong. A raw command log is mostly
/// noise, and a recording that teaches the noise is worse than no recording at all — the model would
/// faithfully reproduce the mistakes.</para>
///
/// <para><b>Undo is the rule that matters most.</b> An Undo does not mean "and then they undid it";
/// it means the step before it never happened, and a demonstration that includes both is a
/// demonstration of nothing. So Undo pops the last surviving step and Redo pushes it back, which is
/// exactly what those keys do to the document.</para>
///
/// <para><b>What survives is judged by EFFECT, not by name.</b> A command is kept because it changed
/// the document — objects added, removed, edited or moved. That one test drops every view command
/// without needing to know their names, and it drops a command that ran but did nothing (a Trim that
/// found no intersection). The name list is a second pass, for the commands that DO change the
/// document but are not part of what is being shown.</para>
///
/// <para><b>A selection is not a step.</b> Selecting is how a command is set up, and the selection
/// that mattered is recorded as that command's INPUT rather than as an operation of its own.</para>
/// </summary>
public static class ModellingRecorder
{
    /// <summary>The name given to a change made without a command.</summary>
    public const string DirectEditName = "Direct edit";

    /// <summary>How many lines of command-line text one step keeps.</summary>
    public const int MaxTranscriptLines = 6;

    /// <summary>
    /// What the model is told to do with a recording, unless the node says otherwise.
    ///
    /// <para>It ends by asking the model to WAIT, deliberately. A demonstration arrives as a user turn
    /// and the natural next thing for a model to do is start applying it — to whatever happens to be
    /// selected, with parameters it inferred and nobody checked. Describing the procedure back first
    /// is what makes the inference correctable while it is still cheap.</para>
    /// </summary>
    public const string DefaultClosing =
        "This is a recording of what the user did by hand, so that you can repeat it. "
        + "Describe the procedure back in your own words, say which parts you are confident about and "
        + "which you had to infer, and say what you would need to know to apply it to other geometry. "
        + "Do not apply it to anything yet.";

    // Judged by name only for commands that DO change the document but are not part of the
    // demonstration. Everything that merely navigates or selects is dropped by the effect test
    // instead, which is why this list is short and does not try to be a Rhino command index.
    private static readonly HashSet<string> NotModelling = new(StringComparer.OrdinalIgnoreCase)
    {
        // Undo and Redo carry their meaning through the marks, not as steps of their own.
        "Undo", "Redo", "UndoMultiple", "RedoMultiple",

        // File and document management. Save changes nothing; Import and Paste do, and are kept.
        "Save", "SaveAs", "SaveSmall", "SaveAsTemplate", "IncrementalSave", "Open", "New", "Revert",
        "Purge", "Audit3dmFile", "Compact",

        // Application state, not the model.
        "Options", "DocumentProperties", "Properties", "Notes", "Layer", "LayerStateManager",
        "Osnap", "Grid", "Units", "Template",

        // Grasshopper and scripting: a solution or a script writing into the document is the
        // PIPELINE acting, not the person demonstrating. The recorder also ignores object events
        // outside a command, which covers most of this; the names cover the rest.
        "Grasshopper", "GrasshopperPlayer", "GrasshopperBake", "GrasshopperUnloadPlugin",
        "ScriptEditor", "EditPythonScript", "RunPythonScript", "EditScript", "RunScript",
    };

    /// <summary>
    /// Whether a command is one a demonstration should ignore even when it changes the document.
    /// </summary>
    /// <param name="commandName">The command's English name.</param>
    /// <returns>True to leave it out.</returns>
    public static bool IsNotModelling(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return true;
        }

        // A scripted or command-line form arrives with a leading underscore or dash ("_Line",
        // "-Offset"), and it is the same command.
        return NotModelling.Contains(commandName.Trim().TrimStart('_', '-'));
    }

    /// <summary>
    /// Distils a raw stream into the procedure that was meant.
    /// </summary>
    /// <param name="entries">Everything observed, oldest first.</param>
    /// <returns>The procedure, with counts of what was thrown away.</returns>
    public static ModellingProcedure Distil(IEnumerable<ModellingEntry>? entries)
    {
        var kept = new List<ProcedureStep>();
        var undone = new Stack<ProcedureStep>();
        int discarded = 0;
        int undoneCount = 0;

        foreach (ModellingEntry entry in entries ?? Array.Empty<ModellingEntry>())
        {
            switch (entry)
            {
                case UndoMark:
                    if (kept.Count > 0)
                    {
                        undone.Push(kept[^1]);
                        kept.RemoveAt(kept.Count - 1);
                        undoneCount++;
                    }

                    break;

                case RedoMark:
                    if (undone.Count > 0)
                    {
                        kept.Add(undone.Pop());
                        undoneCount = Math.Max(0, undoneCount - 1);
                    }

                    break;

                case DirectEditStep direct:
                    if (direct.Delta.IsEmpty)
                    {
                        break;
                    }

                    kept.Add(new ProcedureStep(0, DirectEditName, 1, direct.Delta, Array.Empty<string>()));
                    break;

                case CommandStep command:
                    if (IsNotModelling(command.Name))
                    {
                        break;
                    }

                    if (command.Outcome != CommandOutcome.Success || command.Delta.IsEmpty)
                    {
                        // Cancelled, failed, or ran and did nothing. Counted rather than silently
                        // dropped, because "6 steps, 4 discarded" tells the reader the recording saw
                        // more than it kept — which is the difference between a filter and a bug.
                        discarded++;
                        break;
                    }

                    kept.Add(new ProcedureStep(
                        0,
                        command.Name.Trim().TrimStart('_', '-'),
                        1,
                        command.Delta,
                        Cap(command.Transcript)));
                    break;
            }
        }

        return new ModellingProcedure(Fold(kept), discarded, undoneCount);
    }

    /// <summary>
    /// Writes a procedure out as the payload a signal carries.
    /// </summary>
    /// <param name="procedure">The distilled procedure.</param>
    /// <param name="closing">What to ask the model to do with it; null uses <see cref="DefaultClosing"/>.</param>
    /// <returns>The payload text.</returns>
    public static string Render(ModellingProcedure? procedure, string? closing = null)
    {
        string instruction = string.IsNullOrWhiteSpace(closing) ? DefaultClosing : closing.Trim();

        if (procedure is null || procedure.Steps.Count == 0)
        {
            // Said plainly rather than left blank. A recording that caught nothing usually means the
            // watch was armed after the work, and a person needs to be told that rather than left
            // wondering why the model has nothing to say.
            var empty = new StringBuilder("Nothing was recorded: no command changed the document while the watch was on.");

            if (procedure is { Discarded: > 0 })
            {
                empty.Append(' ')
                    .Append(procedure.Discarded.ToString(CultureInfo.InvariantCulture))
                    .Append(procedure.Discarded == 1 ? " command was" : " commands were")
                    .Append(" seen but left nothing behind (cancelled, or did nothing).");
            }

            return empty.ToString();
        }

        var text = new StringBuilder();

        text.Append("RECORDED MODELLING PROCEDURE — ")
            .Append(procedure.Steps.Count.ToString(CultureInfo.InvariantCulture))
            .Append(procedure.Steps.Count == 1 ? " step" : " steps");

        var asides = new List<string>(2);
        if (procedure.Discarded > 0)
        {
            asides.Add($"{procedure.Discarded.ToString(CultureInfo.InvariantCulture)} cancelled or ineffective");
        }

        if (procedure.Undone > 0)
        {
            asides.Add($"{procedure.Undone.ToString(CultureInfo.InvariantCulture)} undone by the user and left out");
        }

        if (asides.Count > 0)
        {
            text.Append(" (").Append(string.Join(", ", asides)).Append(')');
        }

        text.Append('.');

        foreach (ProcedureStep step in procedure.Steps)
        {
            text.Append(Environment.NewLine)
                .Append(step.Number.ToString(CultureInfo.InvariantCulture))
                .Append(". ")
                .Append(step.Name);

            if (step.Repeats > 1)
            {
                text.Append(" (x").Append(step.Repeats.ToString(CultureInfo.InvariantCulture)).Append(')');
            }

            string what = DescribeDelta(step.Delta);
            if (what.Length > 0)
            {
                text.Append(" — ").Append(what);
            }

            foreach (string line in step.Transcript)
            {
                text.Append(Environment.NewLine).Append("     > ").Append(line);
            }
        }

        text.Append(Environment.NewLine).Append(Environment.NewLine).Append(instruction);

        return text.ToString();
    }

    // Folds consecutive runs of the same command into one step. Ten Offsets in a row are one
    // intention, and listing them ten times buries the shape of the procedure.
    private static IReadOnlyList<ProcedureStep> Fold(List<ProcedureStep> steps)
    {
        var folded = new List<ProcedureStep>(steps.Count);

        foreach (ProcedureStep step in steps)
        {
            if (folded.Count > 0
                && string.Equals(folded[^1].Name, step.Name, StringComparison.OrdinalIgnoreCase))
            {
                ProcedureStep previous = folded[^1];
                folded[^1] = previous with
                {
                    Repeats = previous.Repeats + 1,
                    Delta = previous.Delta.Plus(step.Delta),
                    Transcript = Cap(previous.Transcript.Concat(step.Transcript).ToList()),
                };
                continue;
            }

            folded.Add(step);
        }

        for (int i = 0; i < folded.Count; i++)
        {
            folded[i] = folded[i] with { Number = i + 1 };
        }

        return folded;
    }

    private static string DescribeDelta(ObjectDelta delta)
    {
        var parts = new List<string>(5);

        if (delta.InputCount > 0)
        {
            // "selected", never "in". Rhino does not say which objects a command CONSUMED, only what
            // was selected when it started — and for a creation command that is usually leftover
            // selection from the step before. Wording it as an input claimed a circle was made FROM
            // the three things that happened to be highlighted (seen live). A command that did use
            // its selection is still readable from the counts beside it.
            parts.Add($"{Count(delta.InputCount)} {Kinds(delta.InputTypes)} selected");
        }

        if (delta.Added > 0)
        {
            parts.Add($"{Count(delta.Added)} {Kinds(delta.AddedTypes)} added");
        }

        if (delta.Replaced > 0)
        {
            parts.Add($"{Count(delta.Replaced)} edited");
        }

        if (delta.Transformed > 0)
        {
            parts.Add(delta.TransformSummary is { Length: > 0 } how
                ? $"{Count(delta.Transformed)} {how}"
                : $"{Count(delta.Transformed)} moved");
        }

        if (delta.Removed > 0)
        {
            parts.Add($"{Count(delta.Removed)} deleted");
        }

        if (delta.Layers.Count > 0)
        {
            parts.Add($"on {string.Join(", ", delta.Layers.Take(3).Select(l => $"\"{l}\""))}");
        }

        return string.Join(", ", parts);
    }

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Kinds(IReadOnlyList<string> types) =>
        types.Count == 0 ? "object(s)" : string.Join("/", types.Take(3));

    private static IReadOnlyList<string> Cap(IReadOnlyList<string>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> trimmed = lines
            .Select(l => (l ?? string.Empty).Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return trimmed.Count <= MaxTranscriptLines
            ? trimmed
            : trimmed.Take(MaxTranscriptLines).ToList();
    }
}
