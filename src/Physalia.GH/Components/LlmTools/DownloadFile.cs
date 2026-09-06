// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Files;
using Physalia.Core.Packaging;
using Physalia.Core.Tools;

namespace Physalia.GH.Components;

/// <summary>
/// Lets the model fetch a file into the pipeline's project folder — the node that turns "this data
/// exists at a URL" into "this data is on disk and the definition can use it".
///
/// <para><b>The path on the wire is the point of the node.</b> A Vancouver LiDAR tile is not
/// something to read into a conversation; it is something to import. So the answer goes two ways, as
/// it does on API Call: the model gets a summary — what landed, how big, what the bytes say it is —
/// and the canvas gets the absolute path on <b>Downloaded Files</b>, ready to wire into a File Path
/// param, a script component, or whatever imports it. Without that output the tool would be able to
/// put a file somewhere and no part of the definition could reach it.</para>
///
/// <para><b>Archives are unpacked by default</b>, because open-data portals hand out zipped data
/// constantly and a tool that stops at the zip has not finished the job. What makes that safe is
/// structural rather than a prompt: every entry is contained, every byte is counted, and the entry
/// count is bounded — see <c>ZipSafety</c>. Prompting for each unpack would have been the weaker
/// answer, since a dialog answered by reflex protects nobody.</para>
///
/// <para>Both <b>Ask first</b> switches are ON by default: a download spends somebody else's
/// bandwidth and fills this machine's disk, and unpacking writes a directory tree, so neither
/// happens on a model's word alone until the user has said it may. They are serialized on this node,
/// so switching them off travels with the pipeline and a colleague gets it configured the way its
/// author left it.</para>
///
/// <para>The prompts fail closed, which has one consequence worth knowing: with no chat window open
/// there is nowhere to ask, so a download is denied immediately rather than after a five-minute
/// timeout. That is the right way round — but a pipeline meant to run unattended needs these
/// switched off deliberately.</para>
/// </summary>
public class DownloadFile : LlmToolComponentBase
{
    // Shared: HttpClient is thread-safe, and one per node would exhaust sockets on a pipeline with
    // several. No default timeout — a 400MB file over a slow link is a normal download, not a hang,
    // and the byte budget is what actually bounds this.
    private static readonly HttpClient Client = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private const int InProjectFolder = 1;
    private const int InMaxDownload = 2;

    private const int OutFiles = 2;
    private const int OutStatus = 3;

    private const long BytesPerMb = 1_000_000;

    private static readonly LlmToolDefinition ToolDef = new(
        "download_file",
        "Download a file from a URL into the project folder, so the definition can use it. Use this for "
        + "DATA — a point cloud, a spreadsheet, a zipped dataset, a drawing — anything you want kept as a "
        + "file rather than read as text. To read a web PAGE instead, use read_url; this tool saves bytes "
        + "to disk and does not return the content. A zip is unpacked automatically unless you pass "
        + "extract false. Returns where the file landed, how big it is, and what its leading bytes say it "
        + "actually is — check that: a portal answering a missing file with an error page produces a "
        + "successful download of the wrong thing.",
        "{\"type\":\"object\",\"properties\":{"
        + "\"url\":{\"type\":\"string\",\"description\":\"The absolute http(s) URL to download.\"},"
        + "\"file_name\":{\"type\":\"string\",\"description\":\"What to call it in the project folder. Optional; taken from the URL when omitted. Keep the extension — it is what tells Rhino and Grasshopper how to read the file.\"},"
        + "\"extract\":{\"type\":\"boolean\",\"description\":\"Unpack the file if it is a zip archive. Default true.\",\"default\":true},"
        + "\"overwrite\":{\"type\":\"boolean\",\"description\":\"Replace an existing file of the same name whose size differs. Default false; a file already present at the same size is never re-fetched.\",\"default\":false}"
        + "},\"required\":[\"url\"]}");

    private string? _folder;

    private long _maxBytes = 200 * BytesPerMb;

    // ON by default, both of them. A download is somebody else's bandwidth and this machine's disk,
    // and unpacking writes a directory tree — neither is a thing to do on a model's word alone the
    // first time a pipeline is used. Switch them off per node once you trust what it fetches; the
    // setting is serialized here, so a pipeline shared with a colleague arrives configured the way
    // its author left it.
    private bool _askBeforeDownload = true;

    private bool _askBeforeExtract = true;

    private readonly List<string> _downloaded = new();

    private string _status = "No download yet.";

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadFile"/> class.
    /// </summary>
    public DownloadFile()
        : base(
            "Download File",
            "Download",
            "Lets the model fetch a file — a dataset, a point cloud, a zipped export — into the pipeline's project folder, and puts the path on a wire so the definition can use it.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("E48D0B27-5C91-4A36-BF10-73A2E9D6C458");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "A file the model wants downloaded, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises downloading to the model: a URL in, a file in the project folder out. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "What landed, heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing here poses a viewport or touches the Rhino document, so unlike Take Snapshot there is
    /// no marshalling to <c>RhinoApp.Idle</c> — this is the Read PDF case. Async because a download
    /// runs for as long as a download runs, and because the approval prompt has to be able to sit on
    /// screen without a solution waiting behind it.
    /// </remarks>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    /// <remarks>
    /// A node with no folder resolved advertises NOTHING. The same ruling as an API Call with no
    /// endpoint picked: a tool that fails every call reads to the model as a broken capability rather
    /// than an unconfigured node, and it will keep trying it.
    /// </remarks>
    protected override IReadOnlyList<LlmToolDefinition> Definitions =>
        string.IsNullOrWhiteSpace(this._folder)
            ? Array.Empty<LlmToolDefinition>()
            : new[] { ToolDef };

    /// <inheritdoc/>
    public override string? GroundingDirective =>
        string.IsNullOrWhiteSpace(this._folder)
            ? null
            : "Files you download are saved in " + this._folder + " and stay there. Before downloading "
              + "something, check whether it is already in the project folder. After downloading, the file "
              + "is on disk — read it with read_file if it is text, or work with it by its full path (for "
              + "example through run_rhino_script) if it is not. Do not try to read a large binary file as text.";

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Project Folder", "PF", ProjectFolderInput.InputDescription, GH_ParamAccess.item);
        pManager[InProjectFolder].Optional = true;

        pManager.AddIntegerParameter(
            "Max Download",
            "Max",
            "The biggest single file this node will fetch, in MB. The model's judgement about one file, "
            + "bounded by your budget for all of them — a download past this is refused rather than truncated.",
            GH_ParamAccess.item,
            200);
        pManager[InMaxDownload].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Downloaded Files",
            "F",
            "The full path of every file the last call produced — the download itself, or each file unpacked from it. This is the wire the definition reads: pass it to whatever imports the data.",
            GH_ParamAccess.list);

        pManager.AddTextParameter(
            "Status",
            "St",
            "Which folder this node writes to and how the last download went. A place to look when a call is failing.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);

        // The way out when a host will not serve a program: fetch it in a browser, drop it here.
        ProjectFolderMenu.Append(this, menu, this._folder);
        Menu_AppendSeparator(menu);

        Menu_AppendItem(
            menu,
            "Ask before downloading",
            (_, _) =>
            {
                this._askBeforeDownload = !this._askBeforeDownload;
                this.ExpireSolution(true);
            },
            true,
            this._askBeforeDownload);

        Menu_AppendItem(
            menu,
            "Ask before unpacking an archive",
            (_, _) =>
            {
                this._askBeforeExtract = !this._askBeforeExtract;
                this.ExpireSolution(true);
            },
            true,
            this._askBeforeExtract);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetBoolean("AskBeforeDownload", this._askBeforeDownload);
        writer.SetBoolean("AskBeforeExtract", this._askBeforeExtract);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each flag is seeded with its DEFAULT and only then offered to the archive, because
    /// <c>TryGetBoolean</c> leaves the value alone when the key is absent. Reading into a false seed
    /// — which is what this did while the defaults were off — would silently switch both prompts off
    /// for every node saved before they existed, and for any archive that happens not to carry the
    /// keys. An absent setting has to mean "the default", never "false".
    /// </remarks>
    public override bool Read(GH_IReader reader)
    {
        bool ask = true;
        reader.TryGetBoolean("AskBeforeDownload", ref ask);
        this._askBeforeDownload = ask;

        ask = true;
        reader.TryGetBoolean("AskBeforeExtract", ref ask);
        this._askBeforeExtract = ask;

        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        string? typed = null;
        da.GetData(InProjectFolder, ref typed);
        this._folder = ProjectFolderInput.ResolveOrWarn(this, typed);

        int megabytes = 200;
        da.GetData(InMaxDownload, ref megabytes);
        this._maxBytes = Math.Max(1, megabytes) * BytesPerMb;

        if (string.IsNullOrWhiteSpace(this._folder))
        {
            this.AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "No project folder, so this node advertises nothing to the model. Wire a Project Folder "
                + "grounder into Project Folder, or type a folder name.");
        }
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        // Published here rather than in OnSolveTick, which runs BEFORE the calls and would leave the
        // wire a solve behind the download it is meant to describe.
        da.SetDataList(OutFiles, this._downloaded);
        da.SetData(OutStatus, this.BuildStatus());
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(this._folder))
        {
            return ToolCallResult.Error(
                "This Download File node has no project folder configured, so there is nowhere to save "
                + "anything. Tell the user to wire a Project Folder grounder into it.");
        }

        Args args = ParseArgs(call.InputJson);
        if (string.IsNullOrWhiteSpace(args.Url))
        {
            return ToolCallResult.Error("download_file requires a non-empty 'url'.");
        }

        if (this._askBeforeDownload
            && !await this.AskAsync(
                    "Download File",
                    "The model wants to download a file into this pipeline's project folder.",
                    "From: " + args.Url + "\nInto: " + this._folder,
                    ct)
                .ConfigureAwait(false))
        {
            return ToolCallResult.Error(
                "The user declined this download. Do not retry the same URL; ask them what they would "
                + "like instead.");
        }

        Result<DownloadOutcome, string> result = await FileDownload
            .FetchAsync(args.Url, this._folder!, args.FileName, this._maxBytes, args.Overwrite, Client, ct)
            .ConfigureAwait(false);

        if (!result.IsOk(out DownloadOutcome? outcome, out string? error))
        {
            this._status = "Last download failed: " + error;
            return ToolCallResult.Error(error);
        }

        this._downloaded.Clear();
        this._downloaded.Add(outcome.Path);

        var report = new StringBuilder();
        report.Append(outcome.AlreadyPresent
            ? "\"" + outcome.FileName + "\" was already in the project folder at the same size, so it was not fetched again."
            : "Downloaded \"" + outcome.FileName + "\" (" + FileDownload.Describe(outcome.Bytes) + ").");

        report.Append("\nFull path: ").Append(outcome.Path);

        if (outcome.ContentType is { Length: > 0 })
        {
            report.Append("\nServer content type: ").Append(outcome.ContentType);
        }

        if (outcome.Format is { Length: > 0 })
        {
            report.Append("\nThe file's leading bytes say it is: ").Append(outcome.Format);
        }

        if (outcome.Warning is { Length: > 0 })
        {
            report.Append('\n').Append(outcome.Warning);
        }

        if (args.Extract && FileDownload.IsArchive(outcome.Path))
        {
            await this.ExtractAsync(outcome, report, ct).ConfigureAwait(false);
        }

        report.Append("\n\nThis file is on disk now — its path is also on this node's Downloaded Files "
            + "output, so the definition can read it directly.");

        this._status = report.ToString();

        // The outputs are set from OnSolveEnd, which needs a solve; the base schedules a read pass
        // for the result, and that pass is the one that publishes them.
        return ToolCallResult.Ok(report.ToString());
    }

    private async Task ExtractAsync(DownloadOutcome outcome, StringBuilder report, CancellationToken ct)
    {
        if (this._askBeforeExtract
            && !await this.AskAsync(
                    "Download File",
                    "The model wants to unpack a downloaded archive.",
                    "Archive: " + outcome.FileName + " (" + FileDownload.Describe(outcome.Bytes) + ")\nInto: " + this._folder,
                    ct)
                .ConfigureAwait(false))
        {
            report.Append("\nThe archive was NOT unpacked — the user declined. The zip itself is still on disk.");
            return;
        }

        Result<ZipExtractSummary, string> extracted = FileDownload.Extract(outcome.Path, this._folder!, null, ct);

        if (!extracted.IsOk(out ZipExtractSummary? summary, out string? failure))
        {
            report.Append("\nThe archive could not be unpacked: ").Append(failure);
            return;
        }

        this._downloaded.AddRange(summary.Files);

        report.Append("\nUnpacked ").Append(summary.Files.Count)
            .Append(summary.Files.Count == 1 ? " file" : " files")
            .Append(" (").Append(FileDownload.Describe(summary.TotalBytes)).Append("):");

        foreach (string file in summary.Files.Take(20))
        {
            report.Append("\n  ").Append(System.IO.Path.GetFileName(file));
        }

        if (summary.Files.Count > 20)
        {
            report.Append("\n  …and ").Append(summary.Files.Count - 20).Append(" more.");
        }
    }

    // The card is put up in the chat window, labelled with the harness that asked — the window may
    // well be looking at a different Chat than the pipeline making the request.
    private Task<bool> AskAsync(string title, string summary, string detail, CancellationToken ct) =>
        ToolApprovalBroker.RequestAsync(
            new ToolApprovalRequest(title, summary, detail),
            Harness.PhyDocuments.Harness(this),
            ct);

    private string BuildStatus()
    {
        var status = new StringBuilder();
        status.Append(string.IsNullOrWhiteSpace(this._folder)
            ? "No project folder — this node advertises nothing to the model."
            : "Saving into: " + this._folder);

        status.Append("\nMax download: ").Append(FileDownload.Describe(this._maxBytes));

        if (this._askBeforeDownload || this._askBeforeExtract)
        {
            status.Append("\nAsks first before: ");
            status.Append(string.Join(
                " and ",
                new[]
                {
                    this._askBeforeDownload ? "downloading" : null,
                    this._askBeforeExtract ? "unpacking" : null,
                }.Where(s => s is not null)));
        }

        status.Append("\n\n").Append(this._status);
        return status.ToString();
    }

    private static Args ParseArgs(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return new Args(string.Empty, null, true, false);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Args(inputJson.Trim(), null, true, false);
            }

            return new Args(
                Text(root, "url") ?? string.Empty,
                Text(root, "file_name"),
                Flag(root, "extract", true),
                Flag(root, "overwrite", false));
        }
        catch (JsonException)
        {
            return new Args(inputJson.Trim(), null, true, false);
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private sealed record Args(string Url, string? FileName, bool Extract, bool Overwrite);
}
