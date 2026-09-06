// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Files;
using Physalia.Core.Grounding;
using Physalia.Core.Naming;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Names this pipeline's project folder and tells the model what is in it.
///
/// <para>Two outputs, and the second is what makes this the middle of the arrangement rather than
/// decoration. <b>Grounding</b> goes to the Conversation Log and puts the folder's absolute path and
/// its contents in the prompt. <b>Folder</b> is that same path as text, to wire into Download File,
/// Read File and Read PDF — so the folder is configured once, here, and the tools take it from a wire
/// like any other value. No scanning, no hidden coupling, nothing that stops working outside a
/// harness.</para>
///
/// <para><b>Refreshing it is the interesting part, and it is the Rhino Document grounder's problem
/// again.</b> Grasshopper expires along its own data graph, and a file appearing in a folder is not
/// on that graph — a download lands mid-round, and somebody dropping a survey in from Explorer
/// touches nothing at all. So this watches the folder and marks itself expired, which is enough:
/// it sits upstream of the Conversation Log, so the solve caused by the user's next prompt recomputes
/// it before the prompt is assembled. It must NOT post a ScheduleSolution — inside a harness the
/// sub-document is only re-enabled when its proxy solves, and a disabled document silently drops
/// scheduled callbacks.</para>
/// </summary>
public class ProjectFolderGrounder : PhyBase
{
    // Long enough that unpacking an archive of two hundred files costs one rescan rather than two
    // hundred, short enough that a download is reflected before the user has finished reading the
    // model's reply. Unlike the Rhino grounder — whose handler does nothing but set a flag — this
    // one has to enumerate a directory, so it is rate-limited the way Canvas State is.
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(750);

    private readonly object _gate = new();

    private FileSystemWatcher? _watcher;

    private string? _watched;

    private System.Threading.Timer? _debounce;

    // The folder last resolved, for the right-click menu — which has to answer without a solve.
    // Not _watched: that is cleared whenever watching stops, and the menu should still be able to
    // open a folder on a share that refuses to raise events.
    private string? _resolved;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectFolderGrounder"/> class.
    /// </summary>
    public ProjectFolderGrounder()
        : base(
            "Project Folder",
            "PrjFld",
            "Gives this pipeline a folder of its own for downloads, site data and reference files — and tells the model where it is, so a script can open what is in it.",
            "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A2C74F16-30B8-4E59-9D41-6F8B25C0E7A3");

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        ProjectFolderMenu.Append(this, menu, this._resolved);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Project Folder", "PF", ProjectFolderInput.InputDescription, GH_ParamAccess.item);
        pManager[0].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_Grounding(),
            "Grounding",
            "Gnd",
            "Where this pipeline's files are and what is in there, for the model. Wire into a Conversation Log's Grounding input.",
            GH_ParamAccess.item);

        pManager.AddTextParameter(
            "Folder",
            "F",
            "The resolved folder as an absolute path. Wire into the Project Folder input of Download File, Read File or Read PDF so they all work in the same place.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        this.StopWatching();
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string? typed = null;
        DA.GetData(0, ref typed);

        ProjectPathResolution resolution = ProjectFolderInput.Resolve(this, typed);

        if (!resolution.IsResolved)
        {
            this.StopWatching();
            this._resolved = null;
            this.AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                resolution.ProblemText ?? "No project folder could be resolved.");

            DA.SetData(0, new GH_Grounding(new ProjectFolderGrounding(null, Array.Empty<ProjectFileInfo>(), resolution.ProblemText)));
            return;
        }

        string folder = resolution.FullPath!;
        this._resolved = folder;
        this.Watch(folder);

        IReadOnlyList<ProjectFileInfo> files = FileRead.List(folder)
            .IsOk(out IReadOnlyList<ProjectFileInfo>? listed, out string? problem)
            ? listed
            : Array.Empty<ProjectFileInfo>();

        if (problem is { Length: > 0 })
        {
            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, problem);
        }

        this.Message = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));

        DA.SetData(
            0,
            new GH_Grounding(new ProjectFolderGrounding(folder, files, null, files.Count >= FileRead.MaxListed)));
        DA.SetData(1, folder);
    }

    // Watches the folder for anything appearing, changing or going away. Rebuilt only when the folder
    // itself changes, so an ordinary solve costs nothing.
    private void Watch(string folder)
    {
        lock (this._gate)
        {
            if (string.Equals(this._watched, folder, StringComparison.OrdinalIgnoreCase) && this._watcher is not null)
            {
                return;
            }

            this.StopWatchingLocked();

            try
            {
                Directory.CreateDirectory(folder);

                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                };

                watcher.Created += this.OnFolderChanged;
                watcher.Deleted += this.OnFolderChanged;
                watcher.Renamed += this.OnFolderChanged;
                watcher.Changed += this.OnFolderChanged;
                watcher.EnableRaisingEvents = true;

                this._watcher = watcher;
                this._watched = folder;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A network share that will not raise events, or a folder that cannot be created.
                // The listing is still rebuilt on every solve, so the grounding is merely less
                // eager rather than wrong.
                this._watcher = null;
                this._watched = folder;
            }
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        lock (this._gate)
        {
            this._debounce?.Dispose();
            this._debounce = new System.Threading.Timer(
                _ => this.MarkStale(),
                null,
                Quiet,
                System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    // Marking expired is ENOUGH and is all that is safe. This sits upstream of the Conversation Log,
    // so the solve the next prompt causes recomputes the listing before the prompt is assembled;
    // asking for a solution from a watcher thread, inside a harness sub-document that may be
    // disabled, is the trap the Rhino Document grounder documents.
    private void MarkStale()
    {
        try
        {
            Rhino.RhinoApp.InvokeOnUiThread(new Action(() => this.ExpireSolution(false)));
        }
        catch (Exception)
        {
            // Rhino going away underneath a timer is not worth taking a background thread down for.
        }
    }

    private void StopWatching()
    {
        lock (this._gate)
        {
            this.StopWatchingLocked();
        }
    }

    private void StopWatchingLocked()
    {
        this._debounce?.Dispose();
        this._debounce = null;

        if (this._watcher is not null)
        {
            this._watcher.EnableRaisingEvents = false;
            this._watcher.Created -= this.OnFolderChanged;
            this._watcher.Deleted -= this.OnFolderChanged;
            this._watcher.Renamed -= this.OnFolderChanged;
            this._watcher.Changed -= this.OnFolderChanged;
            this._watcher.Dispose();
            this._watcher = null;
        }

        this._watched = null;
    }
}
