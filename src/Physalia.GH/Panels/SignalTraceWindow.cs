// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Eto.Forms;
using Physalia.Core.Signals;
using Physalia.GH.Diagnostics;

namespace Physalia.GH.Panels;

/// <summary>
/// Live signal-trace report: a master grid of every signal captured by
/// <see cref="SignalTraceLog"/> (sequence, mint time, source, outcome, payload preview, carried
/// content, consumption count) over a detail pane showing the selected signal's full payload,
/// content-block and Instructions summaries, and its consumption timeline. Singleton per Rhino
/// session, opened from the Signal Trace canvas widget.
///
/// <para>Refresh is polled: a <see cref="UITimer"/> compares <see cref="SignalTraceLog.Version"/>
/// each tick and rebinds only on change, so no events cross from Grasshopper solve threads to
/// the UI. Pause freezes the refresh only — capture continues, so resuming shows everything
/// that happened meanwhile. Plain Eto throughout; cross-platform.</para>
/// </summary>
public class SignalTraceWindow : Form
{
    private const int PreviewChars = 120;
    private const double RefreshSeconds = 0.25;

    // Only one trace window may exist per Rhino session. Session-only; nothing serializes.
    private static SignalTraceWindow? _activeWindow;

    private readonly GridView _grid;
    private readonly TextArea _detail;
    private readonly CheckBox _pause;
    private readonly DropDown _outcomeFilter;
    private readonly TextBox _search;
    private readonly UITimer _timer;

    private List<TraceRow> _rows = new();
    private int _lastVersion = -1;
    private long? _selectedSequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalTraceWindow"/> class.
    /// </summary>
    private SignalTraceWindow()
    {
        Title = "Physalia Signal Trace";
        ClientSize = new Eto.Drawing.Size(900, 560);
        Resizable = true;
        Minimizable = true;
        Maximizable = true;
        Owner = Rhino.UI.RhinoEtoApp.MainWindow;

        _pause = new CheckBox { Text = "Pause" };

        var clear = new Button { Text = "Clear" };
        clear.Click += (_, _) =>
        {
            SignalTraceLog.Clear();
            RefreshRows();
        };

        _outcomeFilter = new DropDown();
        _outcomeFilter.Items.Add("All");
        _outcomeFilter.Items.Add("Success");
        _outcomeFilter.Items.Add("Failure");
        _outcomeFilter.SelectedIndex = 0;
        _outcomeFilter.SelectedIndexChanged += (_, _) => RefreshRows();

        _search = new TextBox { PlaceholderText = "Search source / payload…", Width = 220 };
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
        AddColumn("#", 56, r => r.Sequence);
        AddColumn("Time", 92, r => r.Time);
        AddColumn("Source", 150, r => r.Source);
        AddColumn("Outcome", 70, r => r.Outcome);
        AddColumn("Payload", 300, r => r.Preview);
        AddColumn("Carries", 110, r => r.Carries);
        AddColumn("Consumed", 76, r => r.Consumed);
        _grid.SelectionChanged += (_, _) => OnSelectionChanged();

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

    // Polled refresh: rebind only when the log changed and the view is not paused.
    private void Tick()
    {
        if (_pause.Checked == true)
        {
            return;
        }

        if (SignalTraceLog.Version != _lastVersion)
        {
            RefreshRows();
        }
    }

    // Snapshots the log, applies the filters, rebinds the grid, and restores the selection
    // (by sequence number, so a rebind never jumps to a different signal). With nothing
    // selected the grid follows the newest row.
    private void RefreshRows()
    {
        _lastVersion = SignalTraceLog.Version;

        string needle = _search.Text?.Trim() ?? string.Empty;
        int outcome = _outcomeFilter.SelectedIndex;

        _rows = SignalTraceLog.Snapshot()
            .Where(e => outcome switch
            {
                1 => e.Outcome == SignalOutcome.Success,
                2 => e.Outcome == SignalOutcome.Failure,
                _ => true,
            })
            .Where(e => needle.Length == 0
                || e.SourceName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || e.Payload.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(e => new TraceRow(e))
            .ToList();

        _grid.DataStore = _rows;

        int restored = _selectedSequence is { } seq ? _rows.FindIndex(r => r.Entry.Sequence == seq) : -1;
        if (restored >= 0)
        {
            _grid.SelectRow(restored);
            _grid.ScrollToRow(restored);
        }
        else
        {
            _selectedSequence = null;
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

        _selectedSequence = _rows[row].Entry.Sequence;
        _detail.Text = BuildDetail(_rows[row].Entry);
    }

    // Exports the FULL trace (unfiltered — the transcript is the log, not the current view) to a
    // text file, one detail block per signal in sequence order.
    private void ExportTranscript()
    {
        IReadOnlyList<SignalTraceEntry> entries = SignalTraceLog.Snapshot();

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
            File.WriteAllText(dialog.FileName, BuildTranscript(entries));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not write the transcript: {ex.Message}", "Export Signal Transcript", MessageBoxType.Error);
        }
    }

    private static string BuildTranscript(IReadOnlyList<SignalTraceEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Physalia signal transcript — exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {entries.Count} signal(s).");
        sb.AppendLine($"Trace holds the most recent {SignalTraceLog.Capacity} signals of the session; older ones are evicted.");

        foreach (SignalTraceEntry entry in entries)
        {
            sb.AppendLine();
            sb.AppendLine(new string('─', 72));
            sb.Append(BuildDetail(entry));
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

    /// <summary>
    /// One grid row: the traced entry plus its precomputed display strings.
    /// </summary>
    private sealed class TraceRow
    {
        public TraceRow(SignalTraceEntry entry)
        {
            Entry = entry;
            Sequence = entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Time = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            Source = entry.SourceName;
            Outcome = entry.Outcome.ToString();
            Preview = BuildPreview(entry);
            Carries = BuildCarries(entry);
            Consumed = entry.Consumptions.Count == 0 ? "—" : $"{entry.Consumptions.Count}×";
        }

        public SignalTraceEntry Entry { get; }

        public string Sequence { get; }

        public string Time { get; }

        public string Source { get; }

        public string Outcome { get; }

        public string Preview { get; }

        public string Carries { get; }

        public string Consumed { get; }

        private static string BuildPreview(SignalTraceEntry entry)
        {
            string flat = entry.Payload.Replace('\r', ' ').Replace('\n', ' ');
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
