// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Generation;
using Rhino;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that runs a Python 3 script against the active Rhino document. It is
/// the model's hands on Rhino itself — baking geometry, editing layers, reading document state,
/// running analysis — as distinct from the Grasshopper canvas, which the transmitters own.
/// </summary>
/// <remarks>
/// <para><b>It is also the model's eyes.</b> Because the run captures real stdout (see
/// <see cref="RhinoScriptRunner"/>), a script that prints what it found or made hands that straight
/// back as the tool result. That is why this node makes a separate document-inspection tool
/// unnecessary: the model asks its question by writing three lines of Python and gets the answer in
/// the same round, phrased however it needed it.</para>
/// <para><b>Threading.</b> Document mutation is illegal off the main thread and illegal inside a
/// Grasshopper solution, so — exactly as Take Snapshot does for posing a viewport — the run is
/// marshalled to <c>RhinoApp.Idle</c> and awaited off the solve, with a timeout so a Rhino that
/// never goes idle cannot strand the round with the tool's id unanswered.</para>
/// </remarks>
public class RunRhinoScript : LlmToolComponentBase
{
    // Waiting for Idle, not for the script. Once the handler runs, the script owns the UI thread and
    // no token can take it back — a managed thread cannot be aborted — so this bounds the wait to
    // START, which is the part that can hang without anything having gone wrong in the script.
    private const int IdleTimeoutMs = 30_000;

    // The result the model reads has to fit in a turn. Truncation keeps the TAIL, because a script
    // that prints a running log and then a summary line puts the summary last, and the summary is
    // the part worth carrying.
    private const int MaxOutputChars = 20_000;

    private const string DefaultUndoLabel = "Physalia script";

    private static readonly LlmToolDefinition ToolDef = new(
        "run_rhino_script",
        "Run a Python 3 script inside Rhino, against the live document. Full RhinoCommon is available "
        + "(`import Rhino`, `import scriptcontext as sc`, `import rhinoscriptsyntax as rs`); "
        + "`sc.doc` is the active document. The whole script is recorded as a single undo step.\n\n"
        + "USE THIS FOR Rhino-side work: baking or editing geometry in the document, layers, named "
        + "views, units and document settings, selection, and — importantly — READING. Anything you "
        + "want to know about the document you can ask by printing it: everything the script writes "
        + "to stdout comes back to you as the result, so `print` is how you look around.\n\n"
        + "DO NOT use it to build parametric definitions. Geometry that should be driven by the "
        + "Grasshopper graph belongs on the canvas, placed through the normal definition path, where "
        + "it stays editable and gets validated. Reach for this when the work is genuinely Rhino's "
        + "and not the canvas's.\n\n"
        + "Errors come back with the message, the source position and the traceback. A script that "
        + "fails part way through has ALREADY applied what it did before failing — the result says "
        + "how the document's object count moved, so check that before retrying rather than "
        + "assuming nothing happened.",
        "{\"type\":\"object\",\"properties\":{"
        + "\"script\":{\"type\":\"string\",\"description\":\"The Python 3 source to run. Print anything you want reported back to you.\"},"
        + "\"description\":{\"type\":\"string\",\"description\":\"A short label for what this script does, used as the name of the undo step (e.g. 'build balcony railings'). Optional but helpful to the user reading their undo stack.\"}"
        + "},\"required\":[\"script\"]}");

    private readonly object _gate = new();
    private string _lastScript = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunRhinoScript"/> class.
    /// </summary>
    public RunRhinoScript()
        : base(
            "Run Rhino Script",
            "RhinoPy",
            "Lets the model run Python inside Rhino, against the live document — to make and edit "
            + "geometry, drive layers and document settings, or simply to look: whatever the script "
            + "prints comes straight back to it. One undo step per run.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("2F6A9C31-84D7-4B05-9E13-5C7A0D2E8B64");

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    /// <remarks>
    /// Not for speed: running a script must happen on the UI thread and outside a solution, so the
    /// work is deferred to <c>RhinoApp.Idle</c> and awaited off the solve.
    /// </remarks>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "A script the model wants run in Rhino, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises Rhino scripting to the model: Python run against the live Rhino document, with "
        + "whatever it prints handed back. A Tools Present grounder finds this on its own once a "
        + "Router dispatches here, so it needs no wire.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "What the script printed, plus how the document's object count moved and any error it raised.";

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Last Script",
            "LS",
            "The source of the most recent script the model ran, so you can read what it actually "
            + "did. Wire it into a Panel. Empty until the first call.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Published here rather than in <c>OnSolveTick</c>: the calls themselves set it, and the tick
    /// runs before them, so publishing there would leave the wire a solve behind.
    /// </remarks>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        string script;
        lock (_gate)
        {
            script = _lastScript;
        }

        da.SetData(FirstAdditionalOutputIndex, script);
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        string script;
        string label;
        try
        {
            if (!TryReadArguments(call.InputJson, out script, out label, out string argumentError))
            {
                return ToolCallResult.Error(argumentError);
            }
        }
        catch (JsonException ex)
        {
            return ToolCallResult.Error($"run_rhino_script received invalid JSON arguments: {ex.Message}");
        }

        lock (_gate)
        {
            _lastScript = script;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(IdleTimeoutMs);

        RhinoScriptRunner.ScriptOutcome outcome;
        try
        {
            outcome = await RunOnIdleAsync(script, label, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolCallResult.Error(
                "The script timed out waiting for Rhino to become idle — it never started, so the "
                + "document is unchanged. Rhino may be mid-command or showing a dialog; report this "
                + "rather than retrying immediately.");
        }

        string report = Report(outcome);
        return outcome.Completed ? ToolCallResult.Ok(report) : ToolCallResult.Error(report);
    }

    // Runs on the next RhinoApp.Idle: the UI thread, and outside any solution — both required to
    // mutate the document. One-shot, and unhooked on every exit path so a cancelled call leaves no
    // handler behind to fire against a dead component.
    private static Task<RhinoScriptRunner.ScriptOutcome> RunOnIdleAsync(
        string script,
        string label,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<RhinoScriptRunner.ScriptOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler? handler = null;
        CancellationTokenRegistration registration = default;

        handler = (_, _) =>
        {
            RhinoApp.Idle -= handler;
            registration.Dispose();

            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }

            // RhinoScriptRunner.Run never throws for a fault in the script, but the call itself is
            // still guarded: an exception escaping an Idle handler takes Rhino down, not just us.
            try
            {
                tcs.TrySetResult(RhinoScriptRunner.Run(script, label));
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(new RhinoScriptRunner.ScriptOutcome(
                    false, string.Empty, string.Empty, $"{ex.GetType().Name}: {ex.Message}", 0, 0));
            }
        };

        registration = ct.Register(() =>
        {
            RhinoApp.Idle -= handler;
            tcs.TrySetCanceled(ct);
        });

        RhinoApp.Idle += handler;
        return tcs.Task;
    }

    private static bool TryReadArguments(string inputJson, out string script, out string label, out string error)
    {
        script = string.Empty;
        label = DefaultUndoLabel;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(inputJson))
        {
            error = "run_rhino_script was called with no arguments; it needs a 'script'.";
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(inputJson);
        JsonElement root = document.RootElement;

        if (root.ValueKind is not JsonValueKind.Object)
        {
            error = "run_rhino_script expects a JSON object of arguments.";
            return false;
        }

        if (!root.TryGetProperty("script", out JsonElement scriptElement)
            || scriptElement.ValueKind is not JsonValueKind.String)
        {
            error = "run_rhino_script needs a 'script' argument holding the Python 3 source to run.";
            return false;
        }

        script = scriptElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(script))
        {
            error = "run_rhino_script was given an empty script; nothing to run.";
            return false;
        }

        if (root.TryGetProperty("description", out JsonElement labelElement)
            && labelElement.ValueKind is JsonValueKind.String
            && labelElement.GetString() is { } supplied
            && !string.IsNullOrWhiteSpace(supplied))
        {
            label = supplied.Trim();
        }

        return true;
    }

    // The result the model reads. The object-count line is stated on every path including failure,
    // because a half-applied script is the case where the model most needs to know that something
    // happened before it plans a retry.
    private static string Report(RhinoScriptRunner.ScriptOutcome outcome)
    {
        var builder = new StringBuilder();

        builder.Append(outcome.Completed
            ? "Script completed."
            : outcome.Failure ?? "The script failed for an unknown reason.");

        int delta = outcome.ObjectsAfter - outcome.ObjectsBefore;
        builder.AppendLine().AppendLine();
        builder.Append("Objects in document: ")
            .Append(outcome.ObjectsBefore)
            .Append(" -> ")
            .Append(outcome.ObjectsAfter)
            .Append(delta == 0 ? " (unchanged)" : delta > 0 ? $" (+{delta})" : $" ({delta})");

        AppendSection(builder, outcome.Completed ? "output" : "output before the error", outcome.Output);
        AppendSection(builder, "stderr", outcome.Errors);

        if (outcome.Completed && outcome.Output.Length == 0)
        {
            builder.AppendLine().AppendLine();
            builder.Append(
                "The script printed nothing. If you meant to inspect something, print it — stdout is "
                + "how a result reaches you.");
        }

        return builder.ToString();
    }

    private static void AppendSection(StringBuilder builder, string title, string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        builder.AppendLine().AppendLine();
        builder.Append("--- ").Append(title).AppendLine(" ---");
        builder.Append(Truncate(body));
    }

    private static string Truncate(string text)
    {
        if (text.Length <= MaxOutputChars)
        {
            return text;
        }

        int dropped = text.Length - MaxOutputChars;
        return $"[{dropped} earlier characters dropped; showing the last {MaxOutputChars}]"
            + Environment.NewLine
            + text[^MaxOutputChars..];
    }
}
