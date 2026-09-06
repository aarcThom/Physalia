// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Files;

namespace Physalia.GH.Components;

/// <summary>
/// Lets the model read the files in the pipeline's project folder.
///
/// <para><b>Sized honestly: this is for the small files, not the big one.</b> Nothing good comes of
/// putting a 400MB point cloud through a language model, and this tool refuses to try. What it is
/// for is everything around it — the metadata JSON, the tile index naming which file covers which
/// block, the survey CSV, the readme explaining the naming scheme. Those are what tell a model which
/// large file to reach for, and they are small, textual and cheap.</para>
///
/// <para>A binary file is described rather than decoded: name, size, and what its leading bytes say
/// it is. Handing back a screenful of replacement characters costs tokens and reads like a file that
/// is merely empty, which is the wrong conclusion about a perfectly good point cloud.</para>
///
/// <para>Reads are confined to the project folder. That guard catches accidents and bounds cost; it
/// is not a sandbox, and where <c>run_rhino_script</c> is also advertised the model already has the
/// disk through Python. Worth having for what it actually does, not worth describing as more.</para>
/// </summary>
public class ReadFile : LlmToolComponentBase
{
    private const int InProjectFolder = 1;
    private const int OutStatus = 2;

    private static readonly LlmToolDefinition ToolDef = new(
        "read_file",
        "Read the files in this pipeline's project folder. Actions: \"list\" (every file, with sizes), "
        + "\"stat\" (one file's size, format and full path, without reading it), \"text\" (read a text file, "
        + "with offset and max_chars for long ones), \"search\" (find a string in a text file, with line "
        + "numbers). Use this for the small files that explain a project — metadata, indexes, CSVs, readmes. "
        + "It will not read a large binary file such as a point cloud or an image: for those, call \"stat\" to "
        + "get the full path and work with the file itself.",
        "{\"type\":\"object\",\"properties\":{"
        + "\"action\":{\"type\":\"string\",\"enum\":[\"list\",\"stat\",\"text\",\"search\"],\"description\":\"What to do. Start with list if you do not know what is there.\"},"
        + "\"path\":{\"type\":\"string\",\"description\":\"The file, relative to the project folder. Required for stat, text and search.\"},"
        + "\"query\":{\"type\":\"string\",\"description\":\"What to look for. Required for search; matched case-insensitively.\"},"
        + "\"offset\":{\"type\":\"integer\",\"description\":\"For text: the character to start at, for reading a long file in pieces.\",\"default\":0},"
        + "\"max_chars\":{\"type\":\"integer\",\"description\":\"For text: the most characters to return.\",\"default\":8000}"
        + "},\"required\":[\"action\"]}");

    private string? _folder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadFile"/> class.
    /// </summary>
    public ReadFile()
        : base(
            "Read File",
            "ReadFile",
            "Lets the model read the files in the pipeline's project folder — the metadata, indexes and notes that say what the big files are.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3B6C81F4-92E7-4D05-A1C8-5E70D3B94F62");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "A file the model wants to read, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises project-file reading to the model. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "The file contents heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    /// <remarks>
    /// No folder, no tool — the same ruling as Download File and API Call. A read tool that answers
    /// every call with "nothing is configured" teaches the model the project has no files.
    /// </remarks>
    protected override IReadOnlyList<LlmToolDefinition> Definitions =>
        string.IsNullOrWhiteSpace(this._folder)
            ? Array.Empty<LlmToolDefinition>()
            : new[] { ToolDef };

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        ProjectFolderMenu.Append(this, menu, this._folder);
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Project Folder", "PF", ProjectFolderInput.InputDescription, GH_ParamAccess.item);
        pManager[InProjectFolder].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager) =>
        pManager.AddTextParameter(
            "Status",
            "St",
            "Which folder this node reads from, and whether it resolved at all.",
            GH_ParamAccess.item);

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        string? typed = null;
        da.GetData(InProjectFolder, ref typed);
        this._folder = ProjectFolderInput.ResolveOrWarn(this, typed);

        if (string.IsNullOrWhiteSpace(this._folder))
        {
            this.AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "No project folder, so this node advertises nothing to the model. Wire a Project Folder "
                + "grounder into Project Folder, or type a folder name.");
        }

        da.SetData(
            OutStatus,
            string.IsNullOrWhiteSpace(this._folder)
                ? "No project folder — this node advertises nothing to the model."
                : "Reading from: " + this._folder);
    }

    /// <inheritdoc/>
    protected override ToolCallResult ExecuteCall(ToolCallContent call)
    {
        if (string.IsNullOrWhiteSpace(this._folder))
        {
            return ToolCallResult.Error(
                "This Read File node has no project folder configured. Tell the user to wire a Project "
                + "Folder grounder into it.");
        }

        Args args = ParseArgs(call.InputJson);

        return args.Action switch
        {
            "list" => this.List(),
            "stat" => this.Stat(args),
            "text" => this.Text(args),
            "search" => this.Search(args),
            _ => ToolCallResult.Error(
                "Unknown action \"" + args.Action + "\". Use list, stat, text or search."),
        };
    }

    private ToolCallResult List()
    {
        if (!FileRead.List(this._folder).IsOk(out IReadOnlyList<ProjectFileInfo>? files, out string? error))
        {
            return ToolCallResult.Error(error);
        }

        if (files.Count == 0)
        {
            return ToolCallResult.Ok(
                "The project folder (" + this._folder + ") is empty. Nothing has been downloaded or added yet.");
        }

        var report = new StringBuilder();
        report.Append(files.Count).Append(files.Count == 1 ? " file in " : " files in ").Append(this._folder)
            .Append(" (most recently changed first):\n");

        foreach (ProjectFileInfo file in files)
        {
            report.Append("- ").Append(file.Path)
                .Append("  ").Append(FileDownload.Describe(file.Bytes))
                .Append("  ").Append(file.ModifiedUtc.ToString("yyyy-MM-dd HH:mm")).Append(" UTC\n");
        }

        if (files.Count >= FileRead.MaxListed)
        {
            report.Append("\n(Listing stops at ").Append(FileRead.MaxListed).Append(" files; there may be more.)");
        }

        return ToolCallResult.Ok(report.ToString().TrimEnd());
    }

    private ToolCallResult Stat(Args args)
    {
        if (string.IsNullOrWhiteSpace(args.Path))
        {
            return ToolCallResult.Error("stat requires a 'path'.");
        }

        if (!FileRead.Stat(this._folder, args.Path).IsOk(out FileDescription? file, out string? error))
        {
            return ToolCallResult.Error(error);
        }

        var report = new StringBuilder();
        report.Append(file.RelativePath).Append('\n')
            .Append("Full path: ").Append(file.FullPath).Append('\n')
            .Append("Size: ").Append(FileDownload.Describe(file.Bytes)).Append('\n')
            .Append("Modified: ").Append(file.ModifiedUtc.ToString("yyyy-MM-dd HH:mm")).Append(" UTC\n")
            .Append("Content: ").Append(file.IsText ? "text" : file.Format ?? "binary data");

        if (!file.IsText)
        {
            report.Append("\n\nThis is not a text file, so read_file cannot return its contents. Use the full "
                + "path above with a tool that can work with the file itself — run_rhino_script, or a "
                + "Grasshopper component that reads this format.");
        }

        return ToolCallResult.Ok(report.ToString());
    }

    private ToolCallResult Text(Args args)
    {
        if (string.IsNullOrWhiteSpace(args.Path))
        {
            return ToolCallResult.Error("text requires a 'path'.");
        }

        if (!FileRead.ReadText(this._folder, args.Path, args.Offset, args.MaxChars)
            .IsOk(out FileTextResult? text, out string? error))
        {
            return ToolCallResult.Error(error);
        }

        var report = new StringBuilder();
        report.Append(args.Path).Append(" — characters ").Append(text.Start).Append('–').Append(text.End)
            .Append(" of ").Append(text.TotalChars).Append(":\n\n").Append(text.Text);

        if (text.HasMore)
        {
            report.Append("\n\n[There is more. Call again with offset ").Append(text.End).Append(" to continue.]");
        }

        return ToolCallResult.Ok(report.ToString());
    }

    private ToolCallResult Search(Args args)
    {
        if (string.IsNullOrWhiteSpace(args.Path))
        {
            return ToolCallResult.Error("search requires a 'path'.");
        }

        if (!FileRead.Search(this._folder, args.Path, args.Query ?? string.Empty)
            .IsOk(out IReadOnlyList<FileMatch>? matches, out string? error))
        {
            return ToolCallResult.Error(error);
        }

        if (matches.Count == 0)
        {
            return ToolCallResult.Ok("No line in " + args.Path + " contains \"" + args.Query + "\".");
        }

        var report = new StringBuilder();
        report.Append(matches.Count).Append(matches.Count == 1 ? " match in " : " matches in ")
            .Append(args.Path).Append(":\n");

        foreach (FileMatch match in matches)
        {
            report.Append("  line ").Append(match.Line).Append(": ").Append(match.Text).Append('\n');
        }

        if (matches.Count >= FileRead.MaxMatches)
        {
            report.Append("\n(Stopped at ").Append(FileRead.MaxMatches).Append(" matches; there may be more.)");
        }

        return ToolCallResult.Ok(report.ToString().TrimEnd());
    }

    private static Args ParseArgs(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return new Args("list", null, null, 0, FileRead.DefaultMaxChars);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Args("list", null, null, 0, FileRead.DefaultMaxChars);
            }

            return new Args(
                (Text(root, "action") ?? "list").Trim().ToLowerInvariant(),
                Text(root, "path"),
                Text(root, "query"),
                Number(root, "offset", 0),
                Number(root, "max_chars", FileRead.DefaultMaxChars));
        }
        catch (JsonException)
        {
            return new Args("list", null, null, 0, FileRead.DefaultMaxChars);
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int parsed)
            ? parsed
            : fallback;

    private sealed record Args(string Action, string? Path, string? Query, int Offset, int MaxChars);
}
