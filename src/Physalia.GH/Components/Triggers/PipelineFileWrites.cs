// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;

namespace Physalia.GH.Components;

/// <summary>
/// Files the pipeline itself has just written, so a Folder Watcher does not report them back to the
/// model as news.
///
/// <para><b>The loop this exists to break.</b> The model calls <c>download_file</c>; the file lands in
/// the project folder; the watcher sees it and starts a round saying a file appeared; the model, being
/// told a file appeared, has every reason to fetch the next one. That is a loop with a bandwidth bill,
/// and it is not caught by any of the round or stall limits, because every round is genuinely
/// different.</para>
///
/// <para><b>Only tool-driven writes are suppressed, and the distinction is the point.</b> A download
/// the MODEL asked for is already reported to it — the tool result carries the path — so hearing about
/// it again adds nothing. A file the USER fetched through the browser window is the opposite case:
/// the whole reason that window writes into the project folder is that the watcher then hands the file
/// to the model, which is what closes the loop a bot challenge broke. So the browser fetch path
/// deliberately does not register here.</para>
///
/// <para>Entries expire on their own after <see cref="Window"/> rather than being removed by the
/// writer. A watcher's notification arrives after the write, sometimes well after (a large file is
/// reported when the copy finishes), and a writer that cleaned up immediately would be racing the
/// event it was trying to suppress.</para>
/// </summary>
internal static class PipelineFileWrites
{
    /// <summary>
    /// How long a registered write stays suppressed. Generous, because the delay between a write
    /// finishing and a watcher reporting it is not ours to predict; short enough that a file the user
    /// edits by hand a minute later is reported normally.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    private static readonly object Gate = new();

    private static readonly Dictionary<string, DateTime> Written = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records that the pipeline wrote a file, so a watcher ignores the change it caused.
    /// </summary>
    /// <param name="fullPath">The absolute path written.</param>
    internal static void Record(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        lock (Gate)
        {
            Sweep();
            Written[Normalise(fullPath)] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Whether a change to this path was caused by the pipeline itself, recently enough to ignore.
    /// </summary>
    /// <param name="fullPath">The absolute path a watcher is reporting.</param>
    /// <returns>True when the change should not be reported.</returns>
    internal static bool WasPipelineWrite(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        lock (Gate)
        {
            Sweep();
            return Written.ContainsKey(Normalise(fullPath));
        }
    }

    // Called under the lock, on every read and write: the set is small and the alternative is a timer
    // for a housekeeping job that has a natural moment to happen anyway.
    private static void Sweep()
    {
        DateTime cutoff = DateTime.UtcNow - Window;
        List<string>? stale = null;

        foreach (KeyValuePair<string, DateTime> entry in Written)
        {
            if (entry.Value < cutoff)
            {
                (stale ??= new List<string>()).Add(entry.Key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (string key in stale)
        {
            Written.Remove(key);
        }
    }

    private static string Normalise(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception)
        {
            // An unparseable path cannot be matched against a watcher's, but it also cannot be
            // suppressed by accident — returning it as given is the safe direction.
            return path;
        }
    }
}
