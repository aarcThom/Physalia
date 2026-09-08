// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Files;
using Physalia.Core.Naming;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Assembles a system prompt out of a plain-text file sitting in this pipeline's own project folder,
/// plus any extra wording typed on the node.
///
/// <para>The same shape as <see cref="SystemPrompt"/> — a file name resolved to its contents, or
/// inline text taken verbatim, with an Additional Prompt appended at the end — and the difference is
/// where it looks. <see cref="SystemPrompt"/> scans <c>Files/SYSTEM_PROMPTS</c>, which ships with the
/// plug-in and is shared by every pipeline on the machine; this scans the harness's own project
/// folder, which is where the work's material already lives and which travels inside a <c>.phy</c>.
/// That makes it the place for a brief written for THIS job — a site description, an office standard,
/// the wording a firm wants in front of every model on this project — rather than for one of the
/// shipped preambles.</para>
///
/// <para><b>Refreshing it is the Project Folder grounder's problem again.</b> Grasshopper expires
/// along its own data graph and a file appearing in a folder is not on that graph, so the list the
/// Picker offers would go stale the moment somebody dropped a brief in from Explorer. The folder is
/// watched and this node marks itself expired, which is enough: it sits upstream of the Conversation
/// Log, so the solve the next prompt causes rescans before the prompt is assembled. It must NOT post
/// a ScheduleSolution from the watcher — inside a harness the sub-document is only re-enabled when
/// its proxy solves, and a disabled document silently drops scheduled callbacks.</para>
/// </summary>
public class ProjectPrompts : PhyBase, IPickableValuesSource
{
    // The same rate limit the Project Folder grounder uses, for the same reason: this handler has to
    // enumerate a directory, so unpacking an archive of two hundred files must cost one rescan.
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(750);

    private readonly object _gate = new();

    private string _lastPromptFiles = string.Empty;

    private List<string> _promptFiles = new();

    private FileSystemWatcher? _watcher;

    private string? _watched;

    private System.Threading.Timer? _debounce;

    // The folder last resolved, for the right-click menu — which has to answer without a solve.
    private string? _resolved;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectPrompts"/> class.
    /// </summary>
    public ProjectPrompts()
        : base(
            "Project Prompts",
            "Project Prompts",
            "Writes the standing instructions from a text file kept in this pipeline's own project folder, so the wording travels with the work rather than with the plug-in. Wire the result into a Conversation Log.",
            "Pipeline")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3F5A7C21-9E48-4D6B-B0A3-27C1D8E4F962");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs => new[]
    {
        new PickableInput("Prompt File", this._promptFiles),
    };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        if (inputName == "Prompt File")
        {
            this._promptFiles = new List<string>(values);
        }
    }

    /// <inheritdoc/>
    public void ResetValues() => this._promptFiles.Clear();

    /// <summary>
    /// When dropped onto the canvas, auto-places a Picker to the left listing the project folder's
    /// text files. None is placed on a component read out of a file; see
    /// <see cref="PhyBase.AutoPlacePicker"/>.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        this.AutoPlacePicker(document, 0, xOffset: -300f);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        ProjectFolderMenu.Append(this, menu, this._resolved);
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        this.StopWatching();
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Prompt File",
            "PF",
            "The instructions themselves. Either the name of a .txt or .md file in this pipeline's project folder (the Picker placed alongside lists them, and Open Project Folder on this node's menu is where you put one) or the text typed straight in.",
            GH_ParamAccess.item,
            string.Empty);

        pManager.AddTextParameter(
            "Additional Prompt",
            "AP",
            "Anything extra you want to say, added at the end word for word. Always treated as text, never as a filename — this is the place for wording specific to this definition.",
            GH_ParamAccess.item,
            string.Empty);

        pManager[1].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "System Prompt",
            "SP",
            "The file's contents and your additional wording joined into one block of instructions. Wire into a Conversation Log's System Prompt input.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string folder = this.ResolveFolder();

        this.RefreshListIfChanged(folder);

        string promptFile = string.Empty;
        string additional = string.Empty;

        DA.GetData(0, ref promptFile);
        DA.GetData(1, ref additional);

        DA.SetData(0, Assemble(Resolve(folder, promptFile), additional));
    }

    // Joins the two sections the same way the System Prompt component joins its three: a blank line
    // between them, blank sections dropped entirely.
    private static string Assemble(string prompt, string additional)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            parts.Add(prompt.Trim());
        }

        if (!string.IsNullOrWhiteSpace(additional))
        {
            parts.Add(additional.Trim());
        }

        return string.Join("\n\n", parts);
    }

    // If the value names a text file in the project folder, reads it; otherwise it IS the prompt.
    // Resolution goes through FileRead.TryResolve so the containment rule is the one every other
    // project-file node uses — a value landing outside the folder resolves to no file and is
    // therefore sent as typed, never read.
    private static string Resolve(string folder, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || folder.Length == 0)
        {
            return value;
        }

        if (!FileRead.TryResolve(folder, value, out string fullPath, out _) || !IsTextFile(fullPath))
        {
            return value;
        }

        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return value;
        }
    }

    // Only .txt and .md. Deliberately narrower than the System Prompt component's list, which also
    // takes .json/.yaml because a schema lives there: a project folder holds conversation.json,
    // downloads.json and runs.jsonl, and offering the pipeline's own bookkeeping as a system prompt
    // would be noise at best.
    private static bool IsTextFile(string path)
    {
        string ext = Path.GetExtension(path);
        return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase);
    }

    // The sorted .txt/.md names at the top level of the folder. Top level only: a project folder has
    // a PDF subfolder and a conversation-images one, and walking into them would bury the two files
    // this node is actually about.
    private static string GetFileList(string folder)
    {
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            return string.Empty;
        }

        try
        {
            IEnumerable<string> names = Directory
                .EnumerateFiles(folder)
                .Where(IsTextFile)
                .Select(f => Path.GetFileName(f) ?? string.Empty)
                .Where(n => n.Length > 0)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            return string.Join(",", names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    // The harness's own project folder. No Project Folder input on purpose — this node's premise is
    // that the prompt sits with the harness's material — so a component standing on the canvas
    // outside a harness says so rather than quietly reading the fallback folder.
    private string ResolveFolder()
    {
        if (PhyDocuments.Harness(this) is null)
        {
            this.AddRuntimeMessage(
                GH_RuntimeMessageLevel.Remark,
                "This node is not inside a harness, so it has no project folder of its own to read. Put it in a harness, or type the prompt straight in.");
        }

        ProjectPathResolution resolution = ProjectFolderInput.Resolve(this, null);

        if (!resolution.IsResolved)
        {
            this.StopWatching();
            this._resolved = null;
            this.Message = null;
            this.AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                resolution.ProblemText ?? "No project folder could be resolved.");
            return string.Empty;
        }

        string folder = resolution.FullPath!;
        this._resolved = folder;
        this.Message = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        this.Watch(folder);
        return folder;
    }

    // Compares the current file list to the last one and refreshes the Picker when it has changed.
    // Mirrors the System Prompt component's version, including expiring the Picker itself: the
    // Picker solves BEFORE this node, so it has to be told to look again.
    private void RefreshListIfChanged(string folder)
    {
        string current = GetFileList(folder);
        if (current == this._lastPromptFiles)
        {
            return;
        }

        this._lastPromptFiles = current;
        this.SetValues("Prompt File", current.Length > 0 ? current.Split(',') : Array.Empty<string>());

        this.OnPingDocument()?.ScheduleSolution(1, _ =>
        {
            foreach (IGH_Param source in this.Params.Input[0].Sources)
            {
                (source.Attributes?.GetTopLevel?.DocObject as IGH_ActiveObject)?.ExpireSolution(false);
            }

            this.ExpireSolution(true);
        });
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
                // A share that will not raise events, or a folder that cannot be created. The list is
                // still rebuilt on every solve, so the Picker is merely less eager rather than wrong.
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

    // Marking expired is ENOUGH and is all that is safe — see the class remarks.
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
