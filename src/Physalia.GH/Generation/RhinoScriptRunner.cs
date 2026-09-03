// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Diagnostics;
using Rhino.Runtime.Code.Execution;
using Rhino.Runtime.Code.Languages;

namespace Physalia.GH.Generation;

/// <summary>
/// Runs a Python 3 script against the active Rhino document through McNeel's own script engine
/// (<c>Rhino.Runtime.Code</c> — the engine behind the Rhino 8 Script Editor), capturing stdout and
/// stderr as real streams and recording the whole run as one undo step.
/// </summary>
/// <remarks>
/// <para><b>Why in-process rather than through the Rhino MCP server.</b> That server reaches this
/// same engine from outside Rhino, so it can only drive it through <c>_-ScriptEditor _Run</c> on a
/// temp file and recover output by scraping the command window — which is why its tool description
/// has to warn the model not to trust <c>scriptcontext.doc</c>, and why it detects failure by
/// searching the captured text for "Traceback". Standing inside Rhino, this class binds the streams
/// directly, gets a structured <see cref="CompileException"/> or <see cref="ExecuteException"/>
/// instead of string matching, and can wrap the run in an undo record. None of those three are
/// available across the process boundary.</para>
/// <para><b>UI thread only.</b> RhinoCommon is not thread-safe and document mutation must happen on
/// the main thread, so <see cref="Run"/> must be called from it — in practice from a
/// <c>RhinoApp.Idle</c> handler, which is also outside any Grasshopper solution. A long-running
/// script blocks Rhino exactly as the same script would in the Script Editor; that is the engine's
/// own behaviour and there is no safe way to interrupt it, since a managed thread cannot be
/// aborted.</para>
/// </remarks>
public static class RhinoScriptRunner
{
    // Python 3 loads lazily on first use, so a cold QueryLatest can come back null. One forced wait
    // and one retry covers it; the cost lands on the first script of a session only.
    private static ILanguage? _python3;

    /// <summary>
    /// What one script run produced: its captured output, whether it completed, and how the
    /// document's object count moved. Every field is filled on both the success and the failure
    /// path — a script that throws part way through has usually already changed the document, and
    /// the model needs to be told what it managed to do before it failed.
    /// </summary>
    /// <param name="Completed">True when the script ran to the end without raising.</param>
    /// <param name="Output">Everything the script wrote to stdout.</param>
    /// <param name="Errors">Everything the script wrote to stderr, separately from a raised error.</param>
    /// <param name="Failure">The compile or runtime failure, already formatted, or null when it completed.</param>
    /// <param name="ObjectsBefore">Objects in the document immediately before the run.</param>
    /// <param name="ObjectsAfter">Objects in the document immediately after it.</param>
    public sealed record ScriptOutcome(
        bool Completed,
        string Output,
        string Errors,
        string? Failure,
        int ObjectsBefore,
        int ObjectsAfter);

    /// <summary>
    /// Compiles and runs a Python 3 script against the active Rhino document. Never throws for a
    /// fault in the script itself — a compile error, a raised exception and a clean run all come
    /// back as a <see cref="ScriptOutcome"/>, because all three are things the model must be told
    /// rather than things the pipeline should fall over on.
    /// </summary>
    /// <param name="source">The Python 3 source to run.</param>
    /// <param name="undoName">Label for the undo record wrapping the run.</param>
    /// <returns>The captured output and failure state of the run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public static ScriptOutcome Run(string source, string undoName)
    {
        ArgumentNullException.ThrowIfNull(source);

        RhinoDoc? doc = RhinoDoc.ActiveDoc;
        if (doc is null)
        {
            return new ScriptOutcome(
                false, string.Empty, string.Empty, "There is no active Rhino document to run against.", 0, 0);
        }

        if (!TryGetPython3(out ILanguage? language, out string? languageError))
        {
            return new ScriptOutcome(
                false, string.Empty, string.Empty, languageError, doc.Objects.Count, doc.Objects.Count);
        }

        int before = doc.Objects.Count;

        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();

        // The whole script is ONE undo step, so a user who dislikes what the model just did presses
        // Ctrl+Z once rather than once per object. Owned here with an explicit record rather than
        // left to RunContext.RecordDocumentUndo, so the label is ours and the behaviour is the same
        // whatever the engine's own default happens to be.
        uint undo = doc.BeginUndoRecord(undoName);
        string? failure = null;
        try
        {
            // Streams are requested from the constructor AND assigned: the constructor flags make
            // the engine allocate its own defaults, and the assignment then points them at buffers
            // this method can read back.
            var context = new RunContext(defaultOutputStream: true, defaultErrorStream: true)
            {
                OutputStream = stdout,
                ErrorStream = stderr,
                RecordDocumentUndo = false,
            };

            Code code = language!.CreateCode(source);
            code.Run(context);
        }
        catch (CompileException ex)
        {
            failure = FormatCompileFailure(ex);
        }
        catch (ExecuteException ex)
        {
            failure = FormatExecuteFailure(ex);
        }
        catch (Exception ex)
        {
            // A script can reach any RhinoCommon call, so it can raise anything at all. Report it
            // rather than letting it escape into the tool batch and kill the sibling calls.
            failure = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            doc.EndUndoRecord(undo);
        }

        int after = doc.Objects.Count;

        // A script that added or moved geometry has not changed what is on screen until the views
        // are told; the Script Editor redraws on its own and a bare engine run does not.
        doc.Views.Redraw();

        return new ScriptOutcome(
            failure is null,
            ReadStream(stdout),
            ReadStream(stderr),
            failure,
            before,
            after);
    }

    private static bool TryGetPython3(out ILanguage? language, out string? error)
    {
        error = null;
        if (_python3 is not null)
        {
            language = _python3;
            return true;
        }

        try
        {
            _python3 = RhinoCode.Languages.QueryLatest(LanguageSpec.Python3);
            if (_python3 is null)
            {
                // Cold start: the language registry loads engines in the background, so force the
                // wait once and ask again before giving up.
                RhinoCode.Languages.WaitStatusComplete(LanguageSpec.Python3);
                _python3 = RhinoCode.Languages.QueryLatest(LanguageSpec.Python3);
            }
        }
        catch (Exception ex)
        {
            language = null;
            error = $"Rhino's Python 3 engine could not be loaded: {ex.GetType().Name}: {ex.Message}";
            return false;
        }

        language = _python3;
        if (language is null)
        {
            error = "Rhino's Python 3 engine is not available in this Rhino installation.";
            return false;
        }

        return true;
    }

    private static string FormatCompileFailure(CompileException ex)
    {
        var builder = new StringBuilder();
        builder.Append("The script did not compile, so nothing ran.");

        IReadOnlyList<string> lines = DescribeDiagnostics(ex.Diagnosis);
        if (lines.Count > 0)
        {
            builder.AppendLine().Append(string.Join(Environment.NewLine, lines));
        }
        else if (!string.IsNullOrWhiteSpace(ex.Message))
        {
            builder.AppendLine().Append(ex.Message);
        }

        return builder.ToString();
    }

    private static string FormatExecuteFailure(ExecuteException ex)
    {
        var builder = new StringBuilder();
        builder.Append(
            "The script raised an error part way through. Anything it did before that point has "
            + "already been applied to the document.");
        builder.AppendLine().Append(ex.Message);

        // Position and StackTrace are the two things the MCP server's command-window scrape cannot
        // recover reliably, and they are what turns "it failed" into a fixable report.
        string position = SafeToString(() => ex.Position.ToString());
        if (!string.IsNullOrWhiteSpace(position))
        {
            builder.AppendLine().Append("At: ").Append(position);
        }

        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            builder.AppendLine().Append(ex.StackTrace);
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> DescribeDiagnostics(Diagnosis? diagnosis)
    {
        if (diagnosis is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            return diagnosis
                .Select(d => $"{d.Severity}: {d.Message}")
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }
        catch (Exception)
        {
            // The diagnosis is a convenience on the error path; never let reading it replace a real
            // failure message with an exception of its own.
            return Array.Empty<string>();
        }
    }

    private static string SafeToString(Func<string?> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string ReadStream(MemoryStream stream) =>
        stream.Length == 0 ? string.Empty : new UTF8Encoding(false).GetString(stream.ToArray()).TrimEnd();
}
