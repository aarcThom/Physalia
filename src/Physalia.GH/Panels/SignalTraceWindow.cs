// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Eto.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Signals;
using Physalia.GH.Diagnostics;

namespace Physalia.GH.Panels;

/// <summary>
/// Live signal-trace report: a master grid of every signal captured by
/// <see cref="SignalTraceLog"/> (sequence, mint time, source, outcome, payload preview, carried
/// content, consumption count) over a detail pane showing the selected signal's full payload,
/// content-block and Instructions summaries, and its consumption timeline. With Record Messages
/// on, runtime errors/warnings from signal-lifecycle components
/// (<see cref="RuntimeMessageTrace"/>) intersperse the timeline as tinted rows, each carrying
/// how long it was actually displayed — so a transient flash during a solve burst is
/// recognizably ignorable. Export Transcript writes the merged, unfiltered log to a text file.
/// Singleton per Rhino session, opened from the chat window's signal-trace button (wire a Signal
/// Trace human tool into the Conversation Log to get one).
///
/// <para>Refresh is polled: a <see cref="UITimer"/> compares the logs' version counters each
/// tick and rebinds only on change, so no events cross from Grasshopper solve threads to the
/// UI. Pause freezes the refresh only — capture continues, so resuming shows everything that
/// happened meanwhile. Plain Eto throughout; cross-platform.</para>
/// </summary>
public class SignalTraceWindow : Form
{
    private const int PreviewChars = 120;
    private const double RefreshSeconds = 0.25;

    // Row tints distinguishing message rows from signal rows.
    private static readonly Eto.Drawing.Color ErrorTint = Eto.Drawing.Color.FromRgb(0xFFE4E1);
    private static readonly Eto.Drawing.Color WarningTint = Eto.Drawing.Color.FromRgb(0xFFF4DB);

    // Only one trace window may exist per Rhino session. Session-only; nothing serializes.
    private static SignalTraceWindow? _activeWindow;

    private readonly GridView _grid;
    private readonly TextArea _detail;
    private readonly CheckBox _pause;
    private readonly CheckBox _recordMessages;
    private readonly DropDown _outcomeFilter;
    private readonly TextBox _search;
    private readonly UITimer _timer;

    private List<TraceRow> _rows = new();
    private int _lastVersion = -1;
    private string? _selectedKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalTraceWindow"/> class.
    /// </summary>
    private SignalTraceWindow()
    {
        Title = "Physalia Signal Trace";
        ClientSize = new Eto.Drawing.Size(960, 560);
        Resizable = true;
        Minimizable = true;
        Maximizable = true;
        Owner = Rhino.UI.RhinoEtoApp.MainWindow;

        _pause = new CheckBox { Text = "Pause" };

        _recordMessages = new CheckBox { Text = "Record Messages", Checked = RuntimeMessageTrace.Enabled };
        _recordMessages.CheckedChanged += (_, _) =>
        {
            RuntimeMessageTrace.Enabled = _recordMessages.Checked == true;
            RefreshRows();
        };

        var clear = new Button { Text = "Clear" };
        clear.Click += (_, _) =>
        {
            SignalTraceLog.Clear();
            RuntimeMessageTrace.Clear();
            RefreshRows();
        };

        _outcomeFilter = new DropDown();
        _outcomeFilter.Items.Add("All");
        _outcomeFilter.Items.Add("Success");
        _outcomeFilter.Items.Add("Failure");
        _outcomeFilter.SelectedIndex = 0;
        _outcomeFilter.SelectedIndexChanged += (_, _) => RefreshRows();

        _search = new TextBox { PlaceholderText = "Search source / payload…", Width = 200 };
        _search.TextChanged += (_, _) => RefreshRows();

        var export = new Button { Text = "Export Transcript" };
        export.Click += (_, _) => ExportTranscript();

        var toolbar = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _pause,
                _recordMessages,
                clear,
                new Label { Text = "Outcome:" },
                _outcomeFilter,
                _search,
                new StackLayoutItem(null, expand: true),
                export,
            },
        };

        _grid = new GridView
        {
            ShowHeader = true,
            GridLines = GridLines.Horizontal,
            AllowMultipleSelection = false,
        };
        AddColumn("#", 52, r => r.Sequence);
        AddColumn("Time", 90, r => r.Time);
        AddColumn("Source", 145, r => r.Source);
        AddColumn("Outcome", 68, r => r.Outcome);
        AddColumn("Payload", 280, r => r.Preview);
        AddColumn("Carries", 100, r => r.Carries);
        AddColumn("Consumed", 70, r => r.Consumed);
        AddColumn("Shown", 72, r => r.Shown);
        _grid.SelectionChanged += (_, _) => OnSelectionChanged();
        _grid.CellFormatting += (_, e) =>
        {
            if (e.Item is TraceRow { IsMessage: true } row)
            {
                e.BackgroundColor = row.IsError ? ErrorTint : WarningTint;
            }
        };

        _detail = new TextArea
        {
            ReadOnly = true,
            Wrap = true,
            Font = Eto.Drawing.Fonts.Monospace(9),
        };

        var splitter = new Splitter
        {
            Orientation = Orientation.Vertical,
            Position = 320,
            Panel1 = _grid,
            Panel2 = _detail,
        };

        Content = new TableLayout
        {
            Padding = 8,
            Spacing = new Eto.Drawing.Size(0, 8),
            Rows =
            {
                new TableRow(toolbar),
                new TableRow(splitter) { ScaleHeight = true },
            },
        };

        _timer = new UITimer { Interval = RefreshSeconds };
        _timer.Elapsed += (_, _) => Tick();

        Shown += (_, _) =>
        {
            RefreshRows();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// Opens the single trace window, or brings the existing one forward.
    /// </summary>
    public static void ShowOrFocus()
    {
        if (_activeWindow is { } existing)
        {
            existing.BringToFront();
            existing.Focus();
            return;
        }

        var window = new SignalTraceWindow();
        _activeWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeWindow, window))
            {
                _activeWindow = null;
            }
        };
        window.Show();
    }

    private void AddColumn(string header, int width, Func<TraceRow, string> value)
    {
        _grid.Columns.Add(new GridColumn
        {
            HeaderText = header,
            Editable = false,
            Width = width,
            DataCell = new TextBoxCell { Binding = Binding.Delegate(value) },
        });
    }

    // Both logs bump independent version counters; their sum is monotonic and changes iff
    // either log changed.
    private static int CombinedVersion => SignalTraceLog.Version + RuntimeMessageTrace.Version;

    // Polled refresh: rebind only when a log changed and the view is not paused.
    private void Tick()
    {
        if (_pause.Checked == true)
        {
            return;
        }

        if (CombinedVersion != _lastVersion)
        {
            RefreshRows();
        }
    }

    // Snapshots both logs, applies the filters, merges by time, rebinds the grid, and restores
    // the selection (by row key, so a rebind never jumps to a different row). With nothing
    // selected the grid follows the newest row.
    private void RefreshRows()
    {
        _lastVersion = CombinedVersion;

        string needle = _search.Text?.Trim() ?? string.Empty;
        int outcome = _outcomeFilter.SelectedIndex;

        IEnumerable<TraceRow> signals = SignalTraceLog.Snapshot()
            .Where(e => outcome switch
            {
                1 => e.Outcome == SignalOutcome.Success,
                2 => e.Outcome == SignalOutcome.Failure,
                _ => true,
            })
            .Where(e => needle.Length == 0
                || e.SourceName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || e.Payload.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(TraceRow.ForSignal);

        // The outcome filter routes signals; message rows are part of the timeline regardless
        // (only the search narrows them).
        IEnumerable<TraceRow> messages = RuntimeMessageTrace.Snapshot()
            .Where(m => needle.Length == 0
                || m.ComponentName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || m.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(TraceRow.ForMessage);

        _rows = signals.Concat(messages)
            .OrderBy(r => r.SortTimeUtc)
            .ThenBy(r => r.IsMessage)
            .ToList();

        _grid.DataStore = _rows;

        int restored = _selectedKey is { } key ? _rows.FindIndex(r => r.Key == key) : -1;
        if (restored >= 0)
        {
            _grid.SelectRow(restored);
            _grid.ScrollToRow(restored);
        }
        else
        {
            _selectedKey = null;
            _grid.UnselectAll();
            if (_rows.Count > 0)
            {
                _grid.ScrollToRow(_rows.Count - 1);
            }

            _detail.Text = string.Empty;
        }
    }

    private void OnSelectionChanged()
    {
        int row = _grid.SelectedRow;
        if (row < 0 || row >= _rows.Count)
        {
            return;
        }

        TraceRow selected = _rows[row];
        _selectedKey = selected.Key;
        _detail.Text = selected.Message is { } message ? BuildMessageDetail(message) : BuildDetail(selected.Signal!);
    }

    // Exports the FULL merged trace (unfiltered — the transcript is the log, not the current
    // view) to a text file, one block per signal/message in timeline order.
    private void ExportTranscript()
    {
        IReadOnlyList<SignalTraceEntry> signals = SignalTraceLog.Snapshot();
        IReadOnlyList<MessageTraceEntry> messages = RuntimeMessageTrace.Snapshot();

        var dialog = new SaveFileDialog
        {
            Title = "Export Signal Transcript",
            FileName = $"signal-trace-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };
        dialog.Filters.Add(new FileFilter("Text file", ".txt"));

        if (dialog.ShowDialog(this) != DialogResult.Ok || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, BuildTranscript(signals, messages));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not write the transcript: {ex.Message}", "Export Signal Transcript", MessageBoxType.Error);
        }
    }

    private static string BuildTranscript(IReadOnlyList<SignalTraceEntry> signals, IReadOnlyList<MessageTraceEntry> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Physalia signal transcript — exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {signals.Count} signal(s), {messages.Count} runtime message(s).");
        sb.AppendLine($"Trace holds the most recent {SignalTraceLog.Capacity} signals (and {RuntimeMessageTrace.Capacity} messages) of the session; older entries are evicted.");
        sb.AppendLine(RuntimeMessageTrace.Enabled
            ? "Runtime-message recording is ON. A message's 'shown for' is its wall-clock display window sampled at solution ends — a few tens of milliseconds means a transient flash during a solve burst, safely ignorable."
            : "Runtime-message recording is OFF; any messages below are from while it was on.");

        var blocks = signals
            .Select(s => (Time: s.TimestampUtc, IsMessage: false, Text: BuildDetail(s)))
            .Concat(messages.Select(m => (Time: m.StartUtc, IsMessage: true, Text: BuildMessageDetail(m))))
            .OrderBy(b => b.Time)
            .ThenBy(b => b.IsMessage);

        foreach (var block in blocks)
        {
            sb.AppendLine();
            sb.AppendLine(new string('─', 72));
            sb.Append(block.Text);
        }

        return sb.ToString();
    }

    private static string BuildDetail(SignalTraceEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Signal #{entry.Sequence} — {entry.Outcome} from {entry.SourceName} ({entry.SourceId})");
        sb.AppendLine($"Minted {entry.TimestampUtc.ToLocalTime():HH:mm:ss.fff}{(entry.IsStub ? "  [stub: emission not traced — consumed after a Clear]" : string.Empty)}");

        if (entry.Instructions is { } instr)
        {
            sb.AppendLine($"Instructions: system prompt {instr.SystemPromptChars} chars, {instr.TurnCount} turn(s), {instr.ToolCount} tool(s)");
        }

        foreach (ContentBlockSummary block in entry.Blocks)
        {
            sb.AppendLine($"Block: {block.Kind} — {block.Detail}");
        }

        sb.AppendLine();
        sb.AppendLine(entry.Consumptions.Count == 0 ? "Never consumed." : "Consumed:");
        foreach (ConsumptionRecord consumption in entry.Consumptions)
        {
            sb.AppendLine($"  {consumption.TimeUtc.ToLocalTime():HH:mm:ss.fff} → {consumption.ConsumerName} · {consumption.InputName}");
        }

        sb.AppendLine();
        sb.AppendLine(entry.Payload.Length == 0 ? "(empty payload)" : "Payload:");
        if (entry.Payload.Length > 0)
        {
            sb.AppendLine(entry.Payload);
            if (entry.PayloadTruncated)
            {
                sb.AppendLine("… (payload truncated by the trace)");
            }
        }

        return sb.ToString();
    }

    private static string BuildMessageDetail(MessageTraceEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{entry.Level}] on {entry.ComponentName} ({entry.ComponentId})");
        sb.AppendLine(entry.EndUtc is { } end
            ? $"Shown {entry.StartUtc.ToLocalTime():HH:mm:ss.fff} → {end.ToLocalTime():HH:mm:ss.fff} ({FormatDuration(entry.DisplayedFor)})"
            : $"Still showing since {entry.StartUtc.ToLocalTime():HH:mm:ss.fff} ({FormatDuration(entry.DisplayedFor)} so far)");
        sb.AppendLine();
        sb.AppendLine(entry.Text);
        return sb.ToString();
    }

    private static string FormatDuration(TimeSpan span) => span switch
    {
        { TotalMilliseconds: < 1000 } => $"{span.TotalMilliseconds:0} ms",
        { TotalSeconds: < 60 } => $"{span.TotalSeconds:0.0} s",
        _ => $"{(int)span.TotalMinutes}:{span.Seconds:00} min",
    };

    /// <summary>
    /// One grid row — a traced signal or a runtime message — with its precomputed display
    /// strings. Message rows are tinted by the grid's CellFormatting handler.
    /// </summary>
    private sealed class TraceRow
    {
        private TraceRow(SignalTraceEntry? signal, MessageTraceEntry? message)
        {
            Signal = signal;
            Message = message;

            if (signal is not null)
            {
                Key = $"s:{signal.Sequence}";
                SortTimeUtc = signal.TimestampUtc;
                Sequence = signal.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Time = signal.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                Source = signal.SourceName;
                Outcome = signal.Outcome.ToString();
                Preview = Flatten(signal.Payload);
                Carries = BuildCarries(signal);
                Consumed = signal.Consumptions.Count == 0 ? "—" : $"{signal.Consumptions.Count}×";
                Shown = "—";
            }
            else
            {
                MessageTraceEntry m = message!;
                Key = $"m:{m.Id}";
                SortTimeUtc = m.StartUtc;
                Sequence = "—";
                Time = m.StartUtc.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                Source = m.ComponentName;
                Outcome = m.Level.ToString();
                Preview = Flatten(m.Text);
                Carries = "—";
                Consumed = "—";
                Shown = m.EndUtc is null ? "showing…" : FormatDuration(m.DisplayedFor);
            }
        }

        public SignalTraceEntry? Signal { get; }

        public MessageTraceEntry? Message { get; }

        public string Key { get; }

        public DateTime SortTimeUtc { get; }

        public bool IsMessage => Message is not null;

        public bool IsError => Message?.Level == GH_RuntimeMessageLevel.Error;

        public string Sequence { get; }

        public string Time { get; }

        public string Source { get; }

        public string Outcome { get; }

        public string Preview { get; }

        public string Carries { get; }

        public string Consumed { get; }

        public string Shown { get; }

        public static TraceRow ForSignal(SignalTraceEntry entry) => new(entry, null);

        public static TraceRow ForMessage(MessageTraceEntry entry) => new(null, entry);

        private static string Flatten(string text)
        {
            string flat = text.Replace('\r', ' ').Replace('\n', ' ');
            return flat.Length > PreviewChars ? flat[..PreviewChars] + "…" : flat;
        }

        private static string BuildCarries(SignalTraceEntry entry)
        {
            var parts = new List<string>();

            foreach (var group in entry.Blocks.GroupBy(b => b.Kind))
            {
                int n = group.Count();
                parts.Add(n == 1 ? group.Key : $"{n}× {group.Key}");
            }

            if (entry.Instructions is { } instr)
            {
                parts.Add($"Instr({instr.TurnCount}t)");
            }

            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }
    }
}
