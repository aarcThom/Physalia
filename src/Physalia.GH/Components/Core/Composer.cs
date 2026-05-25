// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Physalia.GH.Components;

/// <summary>
/// Assembles a system prompt from a preamble, a JSON schema, and optional tool descriptions.
/// Each section can be supplied as a filename resolved from the Files/SYSTEM_PROMPTS folder,
/// or as inline text wired directly.
/// </summary>
public class Composer : PhyBase
{
    private const string SubfolderPreamble = "PREAMBLE";
    private const string SubfolderSchema = "SCHEMA";
    private const string SubfolderTools = "TOOLS";

    private string _lastPreambleFiles = string.Empty;
    private string _lastSchemaFiles = string.Empty;
    private string _lastToolsFiles = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="Composer"/> class.
    /// </summary>
    public Composer()
        : base("Composer", "Cmp", "Assembles a system prompt from preamble, schema, and tool descriptions.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("BA4FCD24-96DB-4B2B-B7F7-E756A98BC185");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Preamble", "P", "Instruction preamble. Filename from PREAMBLE folder or inline text.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Schema", "S", "JSON schema. Filename from SCHEMA folder or inline text.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Tools", "T", "Tool descriptions. Filename from TOOLS folder or inline text.", GH_ParamAccess.item, string.Empty);

        pManager[2].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("System Prompt", "SP", "Assembled system prompt for Recorder.", GH_ParamAccess.item);
        pManager.AddTextParameter("Schema", "S", "Resolved schema string for Auditor.", GH_ParamAccess.item);
    }

    /// <summary>
    /// When dropped onto the canvas, auto-places three dropdown value lists staggered to the left.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        if (Params.Input[0].SourceCount == 0)
            ComponentHelpers.ValueListAdd(this, document, 0, "Select preamble...", xOffset: -300f, yOffset: -30f);

        if (Params.Input[1].SourceCount == 0)
            ComponentHelpers.ValueListAdd(this, document, 1, "Select schema...", xOffset: -300f, yOffset: 0f);

        if (Params.Input[2].SourceCount == 0)
            ComponentHelpers.ValueListAdd(this, document, 2, "Select tools...", xOffset: -300f, yOffset: 30f);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Save new .composer", OnSaveComposer);
        Menu_AppendItem(menu, "Append to .composer", OnAppendComposer);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        RefreshListIfChanged(0, SubfolderPreamble, ref _lastPreambleFiles);
        RefreshListIfChanged(1, SubfolderSchema, ref _lastSchemaFiles);
        RefreshListIfChanged(2, SubfolderTools, ref _lastToolsFiles);

        string preamble = string.Empty;
        string schema = string.Empty;
        string tools = string.Empty;

        DA.GetData(0, ref preamble);
        DA.GetData(1, ref schema);
        DA.GetData(2, ref tools);

        string resolvedPreamble = Resolve(preamble, SubfolderPreamble);
        string resolvedSchema = Resolve(schema, SubfolderSchema);
        string resolvedTools = Resolve(tools, SubfolderTools);

        DA.SetData(0, Assemble(resolvedPreamble, resolvedSchema, resolvedTools));
        DA.SetData(1, resolvedSchema);
    }

    /// <summary>
    /// Returns the absolute path to <c>Files/SYSTEM_PROMPTS/{subfolder}/</c> beside the assembly.
    /// </summary>
    /// <param name="subfolder">The subfolder name (PREAMBLE, SCHEMA, or TOOLS).</param>
    /// <returns>Absolute directory path, or empty string if the assembly location is unknown.</returns>
    private string GetSubfolderPath(string subfolder)
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (assemblyDir is null) return string.Empty;
        return Path.Combine(assemblyDir, "Files", "SYSTEM_PROMPTS", subfolder);
    }

    /// <summary>
    /// Returns comma-joined sorted filenames for all text files in the subfolder.
    /// Returns an empty string if the directory does not exist.
    /// </summary>
    /// <param name="subfolder">The subfolder name.</param>
    /// <returns>Comma-separated sorted filenames.</returns>
    private string GetFileList(string subfolder)
    {
        string dir = GetSubfolderPath(subfolder);
        if (!Directory.Exists(dir)) return string.Empty;

        IEnumerable<string> names = Directory
            .GetFiles(dir)
            .Where(IsTextFile)
            .Select(f => Path.GetFileName(f) ?? string.Empty)
            .Where(n => n.Length > 0)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        return string.Join(",", names);
    }

    /// <summary>
    /// If <paramref name="input"/> matches a filename in the subfolder, reads and returns that file.
    /// Otherwise returns <paramref name="input"/> unchanged.
    /// </summary>
    /// <param name="input">Filename or inline content.</param>
    /// <param name="subfolder">The subfolder to search.</param>
    /// <returns>Resolved content string.</returns>
    private string Resolve(string input, string subfolder)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string dir = GetSubfolderPath(subfolder);
        if (!Directory.Exists(dir)) return input;

        string candidate = Path.Combine(dir, input);
        if (File.Exists(candidate) && IsTextFile(candidate))
            return File.ReadAllText(candidate);

        return input;
    }

    /// <summary>
    /// Compares the current file list to <paramref name="lastFiles"/>; schedules value-list
    /// repopulation if the list has changed.
    /// </summary>
    /// <param name="paramIndex">Input parameter index.</param>
    /// <param name="subfolder">The subfolder to scan.</param>
    /// <param name="lastFiles">Cached file list from the previous solve.</param>
    private void RefreshListIfChanged(int paramIndex, string subfolder, ref string lastFiles)
    {
        string current = GetFileList(subfolder);
        if (current == lastFiles) return;

        lastFiles = current;
        string[] fileNames = current.Length > 0 ? current.Split(',') : Array.Empty<string>();

        var valueList = Params.Input[paramIndex].Sources.OfType<GH_ValueList>().FirstOrDefault();
        if (valueList is null) return;

        string[] capture = fileNames;
        OnPingDocument()?.ScheduleSolution(1, _ =>
        {
            valueList.ListItems.Clear();
            foreach (string name in capture)
                valueList.ListItems.Add(new GH_ValueListItem(name, $"\"{name}\""));

            if (valueList.ListItems.Count > 0)
                valueList.SelectItem(0);

            valueList.ExpireSolution(true);
        });
    }

    private static string Assemble(string preamble, string schema, string tools)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(preamble))
            parts.Add(preamble.Trim());

        if (!string.IsNullOrWhiteSpace(schema))
        {
            parts.Add("Your response must be valid JSON that conforms exactly to the following schema:");
            parts.Add(schema.Trim());
        }

        if (!string.IsNullOrWhiteSpace(tools))
        {
            parts.Add("The following tools are available:");
            parts.Add(tools.Trim());
        }

        return string.Join("\n\n", parts);
    }

    private static bool IsTextFile(string path)
    {
        string ext = Path.GetExtension(path);
        return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".yml", StringComparison.OrdinalIgnoreCase);
    }

    private void OnSaveComposer(object sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Save new .composer is not yet implemented.");
        ExpireSolution(true);
    }

    private void OnAppendComposer(object sender, EventArgs e)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Append to .composer is not yet implemented.");
        ExpireSolution(true);
    }
}
