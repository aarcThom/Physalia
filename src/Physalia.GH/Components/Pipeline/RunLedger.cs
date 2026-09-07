// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.IO;
using Grasshopper.Kernel;
using Physalia.Core.Recording;

namespace Physalia.GH.Components;

/// <summary>
/// Appends a pipeline's inference calls to <c>runs.jsonl</c> in its project folder.
///
/// <para><b>What the log is for.</b> "What did this pipeline cost, and what was it doing?" is a
/// question that arrives weeks later, from someone reconciling a bill or explaining an afternoon —
/// and until this existed the honest answer was that nothing had been written down. The Budget Guard
/// does not answer it: it bounds what a session MAY spend and forgets everything on close, because
/// that is all a bound needs to do.</para>
///
/// <para><b>Failures are never allowed to cost a round.</b> A project folder on a disconnected share,
/// a read-only directory, a file another process has open — none of that is a reason to lose an
/// answer the model has already produced and the user has already paid for. So every failure here is
/// swallowed, deliberately and without a runtime message: a warning on the node for each of a
/// hundred calls would bury everything else it has to say, and the log is a record rather than part
/// of the work.</para>
///
/// <para><b>Appended, never rewritten.</b> One line per call, which is what makes an append a single
/// write with nothing to read first. See <see cref="RunLog"/> for why the format differs from the
/// download ledger's.</para>
/// </summary>
internal static class RunLedger
{
    /// <summary>
    /// Records one inference call.
    /// </summary>
    /// <param name="component">The LLM Call, used to resolve its pipeline's project folder.</param>
    /// <param name="record">What to record.</param>
    internal static void Append(GH_Component component, RunRecord record)
    {
        try
        {
            Physalia.Core.Naming.ProjectPathResolution resolution =
                ProjectFolderInput.Resolve(component, null);

            if (!resolution.IsResolved || resolution.FullPath is not { Length: > 0 } folder)
            {
                return;
            }

            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, RunLog.FileName);
            File.AppendAllText(path, RunLog.Line(record));

            // A Folder Watcher armed on this project folder must not report the pipeline's own log
            // back to the model — that would be a round per call, which is a loop with a bill.
            PipelineFileWrites.Record(path);
        }
        catch (Exception)
        {
            // Swallowed on purpose. See the class remarks: a log that cannot be written must not cost
            // the answer it was recording.
        }
    }
}
