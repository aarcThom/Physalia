// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Threading;

namespace Physalia.GH.Components;

/// <summary>
/// Marks the spans in which the PIPELINE is writing to the Rhino document, so a Watch Modelling
/// recorder does not mistake the model's own work for the user's demonstration.
///
/// <para><b>Why the command span is not enough.</b> The recorder attributes object events to whatever
/// Rhino command is running, which already excludes most of what the pipeline does. But
/// <c>run_rhino_script</c> and geometry baking are not Rhino commands — their object events arrive
/// with no command open, exactly like a gumball drag, and the gumball case is too common to leave
/// out. So the pipeline says when it is the one writing.</para>
///
/// <para><b>This is the file-write suppression argument again</b>, one document over: what the model
/// did is already in the conversation, so reporting it back as news is at best noise and at worst a
/// loop. There it stops a fetch-everything loop; here it stops a recording that teaches the model its
/// own actions.</para>
///
/// <para>A counter rather than a flag, because these spans nest — a script that runs while another
/// pipeline is baking should not have the inner scope's end re-open recording.</para>
/// </summary>
internal static class PipelineRhinoWrites
{
    private static int _depth;

    /// <summary>
    /// Gets a value indicating whether the pipeline is writing to the Rhino document right now.
    /// </summary>
    internal static bool InProgress => Volatile.Read(ref _depth) > 0;

    /// <summary>
    /// Opens a span in which document changes belong to the pipeline rather than to the user.
    /// </summary>
    /// <returns>A scope to dispose when the writing is done.</returns>
    internal static IDisposable Scope() => new Span();

    private sealed class Span : IDisposable
    {
        private bool _closed;

        internal Span() => Interlocked.Increment(ref _depth);

        public void Dispose()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            Interlocked.Decrement(ref _depth);
        }
    }
}
