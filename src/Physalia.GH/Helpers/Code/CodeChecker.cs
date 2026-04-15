// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Execution;

namespace Physalia.GH.Helpers.Code;

/// <summary>
/// Runs pyflakes against a Python script using RhinoCode.RunScript.
/// Rhino's embedded CPython bundles pyflakes in its site-support directory,
/// which isn't on sys.path by default for RunScript calls. We manually add
/// the site-support path before importing pyflakes. The checker captures
/// stdout/stderr to collect diagnostic output, then returns it as a string
/// for parsing into line/column/message triples.
///
/// To avoid false positives for variables injected at runtime by the Grasshopper
/// component (e.g. brep, count), we prepend stub declarations (var = None) to
/// the script before linting and offset the reported line numbers accordingly.
/// </summary>
public static class CodeChecker
{
    /// <summary>
    /// Runs pyflakes on the given script and returns a list of diagnostics.
    /// Input variable names are prepended as stub declarations to suppress false-positive
    /// undefined-variable warnings for values injected at runtime by Grasshopper.
    /// </summary>
    /// <param name="script">The Python 3 script source to lint.</param>
    /// <param name="inputNames">Variable names injected as inputs at runtime, excluded from lint.</param>
    /// <returns>A list of <see cref="Diagnostic"/> instances describing any pyflakes issues found.</returns>
    public static List<Diagnostic> Check(string script, List<string> inputNames = null)
    {
        var results = new List<Diagnostic>();

        try
        {
            // Build stub lines for injected input variables so pyflakes doesn't
            // report them as undefined
            var stubs = new List<string>();
            if (inputNames != null)
            {
                foreach (var name in inputNames)
                {
                    stubs.Add($"{name} = None");
                }
            }

            int stubLineCount = stubs.Count;
            string stubBlock = stubs.Count > 0 ? string.Join("\n", stubs) + "\n" : string.Empty;
            string lintSource = stubBlock + script;

            var captureScript = @"#! python 3
import sys
import os
import io
import glob

rhinocode_dir = os.path.join(os.path.expanduser('~'), '.rhinocode')
for d in glob.glob(os.path.join(rhinocode_dir, 'py*-rh*')):
    site_support = os.path.join(d, 'site-support')
    if os.path.isdir(site_support) and site_support not in sys.path:
        sys.path.insert(0, site_support)

old_stdout = sys.stdout
old_stderr = sys.stderr
sys.stdout = io.StringIO()
sys.stderr = io.StringIO()

try:
    from pyflakes.api import check
    warning_count = check(source_code, '<physalia>')
    captured_stdout = sys.stdout.getvalue()
    captured_stderr = sys.stderr.getvalue()
    lint_output = captured_stdout + captured_stderr
finally:
    sys.stdout = old_stdout
    sys.stderr = old_stderr
";

            var ctx = new RunContext
            {
                AutoApplyParams = true,
            };

            ctx.Inputs["source_code"] = lintSource;
            ctx.Outputs["lint_output"] = default;

            RhinoCode.RunScript(captureScript, ctx);

            var output = ctx.Outputs.Get<string>("lint_output") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(output))
            {
                return results;
            }

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("<physalia>:"))
                {
                    continue;
                }

                var rest = trimmed["<physalia>:".Length..];

                int row = 0;
                int col = 0;
                string message = rest;

                var parts = rest.Split(':', 3);
                if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out row))
                {
                    if (parts.Length == 3 && int.TryParse(parts[1].Trim(), out col))
                    {
                        message = parts[2].Trim();
                    }
                    else
                    {
                        col = 0;
                        message = string.Join(":", parts, 1, parts.Length - 1).Trim();
                    }
                }

                // Offset line numbers to account for prepended stubs
                row -= stubLineCount;

                // Skip any diagnostics that fall within the stub lines
                if (row <= 0)
                {
                    continue;
                }

                results.Add(new Diagnostic(row, col, message));
            }
        }
        catch (Exception ex)
        {
            results.Add(new Diagnostic(0, 0, $"Lint failed: {ex.Message}"));
        }

        return results;
    }
}

/// <summary>
/// Represents a single pyflakes diagnostic with a line number, column, and message.
/// </summary>
public class Diagnostic
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Diagnostic"/> class.
    /// </summary>
    /// <param name="line">The 1-based line number, or 0 for file-level errors.</param>
    /// <param name="column">The 0-based column offset.</param>
    /// <param name="message">The diagnostic message text.</param>
    public Diagnostic(int line, int column, string message)
    {
        Line = line;
        Column = column;
        Message = message;
    }

    /// <summary>
    /// Gets the 1-based line number at which the issue was reported, or 0 for file-level errors.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the 0-based column offset at which the issue was reported.
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Gets the human-readable diagnostic message from pyflakes.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Returns a formatted string in the form "Line {line}:{col} — {message}", or just the message for file-level errors.
    /// </summary>
    /// <returns>A human-readable summary of this diagnostic.</returns>
    public override string ToString()
    {
        return Line > 0 ? $"Line {Line}:{Column} — {Message}" : Message;
    }
}
