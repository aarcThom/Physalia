// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Triggers;

namespace Physalia.GH.Components;

/// <summary>
/// Fires when files under a folder change, while armed. Hears the change rather than looking for it,
/// so it reacts immediately and costs nothing in between — which is why it, and not the Timer, is the
/// right trigger for anything on this machine's disk.
///
/// <para><b>What it is for.</b> The hand-off that is not a wire: a consultant drops a survey into the
/// project folder, an export lands from another application, a colleague saves a revised schedule.
/// The Project Folder grounder already tells the model what is in the folder; this is what makes
/// something HAPPEN when that changes.</para>
///
/// <para><b>Changed Files is the output that matters.</b> The payload says what changed in prose, for
/// the model; the absolute paths go on the wire, for the definition — a dropped LiDAR tile is to be
/// imported, not read about. Same reasoning as Download File's own path output.</para>
///
/// <para><b>It does not report the pipeline's own downloads.</b> A file <c>download_file</c> just
/// fetched is already reported to the model by the tool result, and waking a round to announce it is
/// how a fetch-one-file round becomes a fetch-every-file loop. A file the USER fetched through the
/// browser window IS reported — that is the whole mechanism by which a blocked download reaches the
/// model. See <see cref="PipelineFileWrites"/>.</para>
/// </summary>
public class FolderTrigger : SignalSourceBase<FolderChange>
{
    private const int InFolder = 0;
    private const int InFilter = 1;
    private const int InSubfolders = 2;
    private const int InSettle = 3;

    private static readonly int OutChangedFiles = FirstAdditionalOutputIndex;

    private FileSystemWatcher? _watcher;

    private string? _folder;

    private string _filter = string.Empty;

    private bool _subfolders;

    private double _settleSeconds = 1.0;

    // The paths from the last fired batch, republished every solve so the wire holds them rather than
    // blanking between solves.
    private List<string> _changedFiles = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderTrigger"/> class.
    /// </summary>
    public FolderTrigger()
        : base(
            "Folder Watcher",
            "Watch",
            "Starts a round when files in a folder appear, change or go away. Right-click and tick Armed to switch it on — it is always off when a file opens. The paths come out on Changed Files so the definition can read what arrived.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6E1D93B4-7A25-4C08-B93F-5D82A14E7C60");

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "Fires once per burst of changes, carrying a description of what changed. Wire into a Conversation Log's Prompt Signal input, or into whatever should look at the new files.";

    /// <inheritdoc/>
    protected override string ArmedCaption
    {
        get
        {
            if (_folder is null)
            {
                return "no folder";
            }

            string where = new DirectoryInfo(_folder).Name;
            return string.IsNullOrWhiteSpace(_filter) ? where : $"{where} · {_filter}";
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Read from the node, because the right window depends on what is being watched: a text file
    /// saved by an editor settles in milliseconds, a multi-gigabyte point cloud copied across a
    /// network share raises events for minutes and must be reported ONCE, when it has finished.
    /// </remarks>
    protected override int SettleMs => (int)Math.Round(Math.Max(0.05, _settleSeconds) * 1000.0);

    /// <inheritdoc/>
    protected override void RegisterSourceInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Project Folder", "F", ProjectFolderInput.InputDescription, GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter(
            "Filter",
            "P",
            "Which files to care about — \"*.las\", \"*.csv;*.txt\", \"site-*.json\". Blank means every file, which is what to leave it as unless the folder is noisy.",
            GH_ParamAccess.item,
            string.Empty);
        pManager.AddBooleanParameter(
            "Subfolders",
            "S",
            "Watch folders inside this one as well.",
            GH_ParamAccess.item,
            false);
        pManager.AddNumberParameter(
            "Settle",
            "T",
            "Seconds of quiet before the change is reported. Long enough that a big file being copied is reported once, when it lands, rather than every time the copy grows.",
            GH_ParamAccess.item,
            1.0);
        pManager[InFolder].Optional = true;
        pManager[InFilter].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Changed Files",
            "CF",
            "The full paths of the files in the last reported change, one per item. Wire into whatever reads them — an importer, a Read File, a script.",
            GH_ParamAccess.list);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        string typed = string.Empty;
        string filter = string.Empty;
        bool subfolders = false;
        double settle = 1.0;
        da.GetData(InFolder, ref typed);
        da.GetData(InFilter, ref filter);
        da.GetData(InSubfolders, ref subfolders);
        da.GetData(InSettle, ref settle);

        _filter = filter ?? string.Empty;
        _settleSeconds = settle;

        string? folder = ProjectFolderInput.ResolveOrWarn(this, typed);

        bool changed = !string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase)
            || subfolders != _subfolders;

        _folder = folder;
        _subfolders = subfolders;

        if (changed)
        {
            RestartListening();
            UpdateStateDisplay();
        }

        if (IsArmed && _watcher is null && _folder is not null)
        {
            // Armed, with a folder, and nothing listening: the folder appeared since arming (a project
            // folder is created lazily), so pick it up now rather than staying silently inert.
            StartListening();
        }
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        da.SetDataList(OutChangedFiles, _changedFiles);
    }

    /// <inheritdoc/>
    protected override void StartListening()
    {
        if (_folder is null || !Directory.Exists(_folder))
        {
            // Not an error: a project folder is created when something first writes to it, and the
            // solve hook picks the watcher up once it exists.
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(_folder)
            {
                IncludeSubdirectories = _subfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            watcher.Created += OnCreated;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
        }
        catch (Exception ex)
        {
            // A folder on a share that vanished, or one the user has no rights to. Say so on the node
            // rather than leaving an armed trigger that can never fire.
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not watch \"{_folder}\": {ex.Message}");
            _watcher = null;
        }
    }

    /// <inheritdoc/>
    protected override void StopListening()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnCreated;
        _watcher.Changed -= OnChanged;
        _watcher.Deleted -= OnDeleted;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
        _watcher = null;
    }

    /// <inheritdoc/>
    protected override string ComposePayload(IReadOnlyList<FolderChange> events)
    {
        IReadOnlyList<FolderChange> folded = FolderChangeSummary.Coalesce(events);

        if (folded.Count == 0)
        {
            // Everything cancelled out — a temporary file that appeared and went. Composing to nothing
            // is the base's contract for "no event after all".
            _changedFiles = new List<string>();
            return string.Empty;
        }

        string root = _folder ?? string.Empty;

        // Removed files are left off the wire: the path of something that is no longer there is a
        // trap for anything downstream that would try to read it. The payload still says it went.
        _changedFiles = folded
            .Where(c => c.Kind != FolderChangeKind.Removed)
            .Select(c => Path.GetFullPath(Path.Combine(root, c.RelativePath)))
            .ToList();

        return FolderChangeSummary.Describe(root, folded);
    }

    private void OnCreated(object sender, FileSystemEventArgs e) => Report(e.FullPath, FolderChangeKind.Added);

    private void OnChanged(object sender, FileSystemEventArgs e) => Report(e.FullPath, FolderChangeKind.Changed);

    private void OnDeleted(object sender, FileSystemEventArgs e) => Report(e.FullPath, FolderChangeKind.Removed);

    private void OnRenamed(object sender, RenamedEventArgs e) => Report(e.FullPath, FolderChangeKind.Renamed);

    private void Report(string fullPath, FolderChangeKind kind)
    {
        if (_folder is null)
        {
            return;
        }

        // A directory raises the same events as a file. Reporting one would put a folder path on the
        // Changed Files wire, where everything downstream expects to be able to open it. Existence is
        // the only test available on a Deleted event, and a deleted directory reads as neither, so it
        // is treated as a file and its name filtered on — which is what keeps it out in practice.
        try
        {
            if (Directory.Exists(fullPath))
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        string name = Path.GetFileName(fullPath);
        if (!TriggerFilter.Matches(name, _filter))
        {
            return;
        }

        if (PipelineFileWrites.WasPipelineWrite(fullPath))
        {
            // The model's own download. It has already been told; see the class remarks.
            return;
        }

        ReportEvent(new FolderChange(Relative(fullPath), kind));
    }

    private string Relative(string fullPath)
    {
        string root = _folder ?? string.Empty;

        try
        {
            string relative = Path.GetRelativePath(root, fullPath);
            return relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(fullPath) : relative;
        }
        catch (Exception)
        {
            return Path.GetFileName(fullPath);
        }
    }
}
