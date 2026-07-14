// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Components;

namespace Physalia.GH.Diagnostics;

/// <summary>
/// Recorder of runtime errors and warnings on signal-lifecycle components
/// (<see cref="StatefulComponentBase"/> subclasses), interspersed with the signal trace by time
/// in the trace window. ON by default (enabled at plugin load — a truncated LLM response or a
/// transient pipeline error must land in the trace without the user having opted in first);
/// toggleable from the trace window, and recording continues while the window is closed (the
/// toggle is process state, like the signal log itself).
///
/// <para>Presence is sampled at every <c>GH_Document.SolutionEnd</c> of the active document:
/// a message first seen at one solution end opens a record, and its disappearance at a later
/// solution end closes it — so the recorded duration is the wall-clock window the message was
/// actually displayed. Physalia's scheduled solves are separate solutions, so an error that
/// flashes mid-burst records a duration of tens of milliseconds and is recognizably transient,
/// while a persistent error keeps accumulating. Disabling recording (or switching documents)
/// closes all open records at that moment. Session-only, capped, never serialized.</para>
/// </summary>
internal static class RuntimeMessageTrace
{
    /// <summary>Maximum number of message records retained; the oldest is evicted past this.</summary>
    internal const int Capacity = 500;

    private static readonly object Gate = new();
    private static readonly List<MessageTraceEntry> Records = new();
    private static readonly Dictionary<string, long> OpenIds = new();

    private static bool _enabled;
    private static long _nextId;
    private static int _version;
    private static GH_Document? _hookedDocument;
    private static bool _canvasHooked;

    /// <summary>
    /// Gets the mutation counter, bumped whenever a record opens, closes, or the trace clears.
    /// The trace window polls this to decide when to re-read <see cref="Snapshot"/>.
    /// </summary>
    internal static int Version
    {
        get
        {
            lock (Gate)
            {
                return _version;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether message recording is on. Enabling hooks the
    /// active document's solution end (and follows document switches); disabling unhooks and
    /// closes every open record at that moment. Set from the UI thread (the trace window
    /// toggle) — Grasshopper event hookup is not thread-safe.
    /// </summary>
    internal static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;

            if (value)
            {
                HookCanvas();
                HookDocument(Instances.ActiveCanvas?.Document);
            }
            else
            {
                HookDocument(null);
                CloseAll(DateTime.UtcNow);
            }
        }
    }

    /// <summary>
    /// Takes an immutable snapshot of every message record in arrival order (oldest first).
    /// Records are immutable, so the returned references are safe to read on any thread.
    /// </summary>
    /// <returns>The message records, oldest first.</returns>
    internal static IReadOnlyList<MessageTraceEntry> Snapshot()
    {
        lock (Gate)
        {
            return Records.ToList();
        }
    }

    /// <summary>
    /// Drops every message record. Messages still displayed on the canvas re-open as fresh
    /// records at the next solution end.
    /// </summary>
    internal static void Clear()
    {
        lock (Gate)
        {
            Records.Clear();
            OpenIds.Clear();
            _version++;
        }
    }

    // Subscribes to active-canvas document switches once per process; safe to call repeatedly.
    // When recording is enabled before any canvas exists (the plugin-load default), the hookup
    // is deferred until Grasshopper creates one.
    private static void HookCanvas()
    {
        if (_canvasHooked)
        {
            return;
        }

        if (Instances.ActiveCanvas is not { } canvas)
        {
            Instances.CanvasCreated -= OnCanvasCreated;
            Instances.CanvasCreated += OnCanvasCreated;
            return;
        }

        canvas.DocumentChanged += OnDocumentChanged;
        _canvasHooked = true;
    }

    private static void OnCanvasCreated(GH_Canvas canvas)
    {
        Instances.CanvasCreated -= OnCanvasCreated;

        if (!_enabled || _canvasHooked)
        {
            return;
        }

        canvas.DocumentChanged += OnDocumentChanged;
        _canvasHooked = true;
        HookDocument(canvas.Document);
    }

    private static void OnDocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e)
    {
        if (_enabled)
        {
            // The old document's messages are no longer on screen — close them honestly.
            CloseAll(DateTime.UtcNow);
            HookDocument(e.NewDocument);
        }
    }

    // Moves the SolutionEnd subscription to the given document (null = unhook only).
    private static void HookDocument(GH_Document? document)
    {
        if (ReferenceEquals(_hookedDocument, document))
        {
            return;
        }

        if (_hookedDocument is not null)
        {
            _hookedDocument.SolutionEnd -= OnSolutionEnd;
        }

        _hookedDocument = document;

        if (document is not null)
        {
            document.SolutionEnd += OnSolutionEnd;
            Scan(document);
        }
    }

    private static void OnSolutionEnd(object sender, GH_SolutionEventArgs e) => Scan(e.Document);

    // Diffs the currently displayed errors/warnings on signal-lifecycle components against the
    // open records: new messages open a record, vanished messages close theirs.
    private static void Scan(GH_Document document)
    {
        DateTime now = DateTime.UtcNow;
        var present = new Dictionary<string, (StatefulComponentBase Component, GH_RuntimeMessageLevel Level, string Text)>();

        foreach (IGH_DocumentObject obj in document.Objects)
        {
            if (obj is not StatefulComponentBase component)
            {
                continue;
            }

            CollectLevel(present, component, GH_RuntimeMessageLevel.Error);
            CollectLevel(present, component, GH_RuntimeMessageLevel.Warning);
        }

        lock (Gate)
        {
            bool changed = false;

            foreach ((string key, var msg) in present)
            {
                if (OpenIds.ContainsKey(key))
                {
                    continue;
                }

                var entry = new MessageTraceEntry(_nextId++, msg.Component.InstanceGuid, msg.Component.Name, msg.Level, msg.Text, now);
                Records.Add(entry);
                OpenIds[key] = entry.Id;
                changed = true;

                while (Records.Count > Capacity)
                {
                    MessageTraceEntry evicted = Records[0];
                    Records.RemoveAt(0);
                    string? evictedKey = OpenIds.FirstOrDefault(p => p.Value == evicted.Id).Key;
                    if (evictedKey is not null)
                    {
                        OpenIds.Remove(evictedKey);
                    }
                }
            }

            foreach (string key in OpenIds.Keys.Where(k => !present.ContainsKey(k)).ToList())
            {
                CloseLocked(key, now);
                changed = true;
            }

            if (changed)
            {
                _version++;
            }
        }
    }

    private static void CollectLevel(
        Dictionary<string, (StatefulComponentBase, GH_RuntimeMessageLevel, string)> present,
        StatefulComponentBase component,
        GH_RuntimeMessageLevel level)
    {
        foreach (string text in component.RuntimeMessages(level))
        {
            // Key by component + level + text: the same text re-added next solve is the same
            // continuing message, not a new one.
            present[$"{component.InstanceGuid}|{level}|{text}"] = (component, level, text);
        }
    }

    private static void CloseAll(DateTime now)
    {
        lock (Gate)
        {
            if (OpenIds.Count == 0)
            {
                return;
            }

            foreach (string key in OpenIds.Keys.ToList())
            {
                CloseLocked(key, now);
            }

            _version++;
        }
    }

    // Stamps EndUtc on an open record. Caller holds the lock.
    private static void CloseLocked(string key, DateTime now)
    {
        long id = OpenIds[key];
        OpenIds.Remove(key);

        int index = Records.FindLastIndex(r => r.Id == id);
        if (index >= 0)
        {
            Records[index] = Records[index] with { EndUtc = now };
        }
    }
}
