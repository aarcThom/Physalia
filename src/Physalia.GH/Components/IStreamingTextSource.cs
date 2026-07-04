// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components;

/// <summary>
/// Implemented by a component that produces an LLM response incrementally and exposes the
/// text accumulated so far while a run is in flight. The Chatbox window reads this from the busy
/// component wired to its Recorder to render the response live as it streams — purely a
/// paint-time read, with no output set and no signal minted until the run actually completes.
/// </summary>
public interface IStreamingTextSource
{
    /// <summary>
    /// Gets the partial response text accumulated so far this run, or null when nothing has
    /// streamed yet. Safe to read from the UI thread while the run streams on a background
    /// thread; resets at the start of each run.
    /// </summary>
    string? StreamingText { get; }
}
