// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// Assembles a system prompt from a preamble and a JSON schema, plus optional free text
/// appended at the end.
/// Each section can be supplied as a filename resolved from the SYSTEM_PROMPTS folder — the user's
/// own copy first, then the one shipped with the plug-in — or as inline text wired directly. The
/// additional prompt is always taken verbatim.
/// </summary>
public class SystemPrompt : PhyBase, IPickableValuesSource
{
    private const string SubfolderPreamble = "PREAMBLE";
    private const string SubfolderSchema = "SCHEMA";

    private string _lastPreambleFiles = string.Empty;
    private string _lastSchemaFiles = string.Empty;

    private List<string> _preambleFiles = new();
    private List<string> _schemaFiles = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPrompt"/> class.
    /// </summary>
    public SystemPrompt()
        : base("System Prompt", "System Prompt", "Writes the standing instructions the model is given on every turn: a preamble, the JSON shape its answers must take, and any extra wording you add. Wire the result into a Conversation Log.", "Pipeline")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("BA4FCD24-96DB-4B2B-B7F7-E756A98BC185");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs => new[]
    {
        new PickableInput("Preamble", _preambleFiles),
        new PickableInput("Schema", _schemaFiles),
    };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        switch (inputName)
        {
            case "Preamble": _preambleFiles = new List<string>(values); break;
            case "Schema": _schemaFiles = new List<string>(values); break;
        }
    }

    /// <inheritdoc/>
    public void ResetValues()
    {
        _preambleFiles.Clear();
        _schemaFiles.Clear();
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Preamble", "P", "The instructions themselves — who the model is and how it should work. Either the name of a file in SYSTEM_PROMPTS/PREAMBLE (the Picker placed alongside lists them) or the text typed straight in.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Schema", "S", "The JSON shape answers must follow, so a Schema Validator can check them. Either the name of a file in SYSTEM_PROMPTS/SCHEMA (the Picker placed alongside lists them) or the schema typed straight in.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Additional Prompt", "AP", "Anything extra you want to say, added at the end word for word. Always treated as text, never as a filename — this is the place for wording specific to this definition.", GH_ParamAccess.item, string.Empty);
        pManager[2].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Schema", "S", "The schema on its own, with any filename already resolved to its contents. Wire into a Schema Validator so it checks answers against the same schema the model was given.", GH_ParamAccess.item);
        pManager.AddTextParameter("System Prompt", "SP", "The three pieces joined into one block of instructions. Wire into a Conversation Log's System Prompt input.", GH_ParamAccess.item);
    }

    /// <summary>
    /// When dropped onto the canvas, auto-places two Pickers staggered to the left — one for the
    /// preamble file and one for the schema. Neither is placed on a component read out of a file;
    /// see <see cref="PhyBase.AutoPlacePicker"/>.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        AutoPlacePicker(document, 0, xOffset: -300f, yOffset: -15f);
        AutoPlacePicker(document, 1, xOffset: -300f, yOffset: 15f);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        RefreshListIfChanged(0, SubfolderPreamble, "Preamble", ref _lastPreambleFiles);
        RefreshListIfChanged(1, SubfolderSchema, "Schema", ref _lastSchemaFiles);

        string preamble = string.Empty;
        string schema = string.Empty;
        string additional = string.Empty;

        DA.GetData(0, ref preamble);
        DA.GetData(1, ref schema);
        DA.GetData(2, ref additional);

        string resolvedPreamble = Resolve(preamble, SubfolderPreamble);
        string resolvedSchema = Resolve(schema, SubfolderSchema);

        string systemPrompt = Assemble(resolvedPreamble, resolvedSchema, additional);

        DA.SetData(0, resolvedSchema);
        DA.SetData(1, systemPrompt);
    }

    /// <summary>
    /// Returns the <c>SYSTEM_PROMPTS/{subfolder}/</c> directories to look in, in precedence order.
    /// </summary>
    /// <param name="subfolder">The subfolder name (PREAMBLE, SCHEMA, or TOOLS).</param>
    /// <returns>One or two absolute directory paths, the user's own first. Neither need exist.</returns>
    /// <remarks>
    /// The user's data folder OVERLAYS the shipped prompts rather than replacing them: a file the
    /// user writes shadows a shipped file of the same name, and everything they have not touched
    /// keeps coming from the package — so a fix to a shipped preamble still reaches them on the next
    /// update. Copying the shipped set into the data folder instead would have forced a choice
    /// between overwriting their edits and never delivering that fix.
    /// </remarks>
    private static IReadOnlyList<string> GetSubfolderPaths(string subfolder) =>
        PhyData.SearchPath(Assembly.GetExecutingAssembly(), PhyData.SystemPrompts)
            .Select(root => Path.Combine(root, subfolder))
            .ToList();

    /// <summary>
    /// Returns comma-joined sorted filenames for all text files in the subfolder, the user's own
    /// shadowing a shipped file of the same name so each name is listed once.
    /// Returns an empty string if no directory exists.
    /// </summary>
    /// <param name="subfolder">The subfolder name.</param>
    /// <returns>Comma-separated sorted filenames.</returns>
    private string GetFileList(string subfolder)
    {
        var names = new List<string>();

        foreach (string dir in GetSubfolderPaths(subfolder))
        {
            if (!Directory.Exists(dir)) continue;

            names.AddRange(Directory
                .GetFiles(dir)
                .Where(IsTextFile)
                .Select(f => Path.GetFileName(f) ?? string.Empty)
                .Where(n => n.Length > 0));
        }

        return string.Join(
            ",",
            names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
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

        // First hit wins, and the user's own folder is searched first — that is what makes an edited
        // copy of a shipped preamble take effect.
        foreach (string dir in GetSubfolderPaths(subfolder))
        {
            if (!Directory.Exists(dir)) continue;

            string candidate = Path.Combine(dir, input);
            if (File.Exists(candidate) && IsTextFile(candidate))
                return File.ReadAllText(candidate);
        }

        return input;
    }

    /// <summary>
    /// Compares the current file list to <paramref name="lastFiles"/>; schedules Picker
    /// refresh if the list has changed.
    /// </summary>
    /// <param name="paramIndex">Input parameter index.</param>
    /// <param name="subfolder">The subfolder to scan.</param>
    /// <param name="inputName">The PickableInput name matching this parameter.</param>
    /// <param name="lastFiles">Cached file list from the previous solve.</param>
    private void RefreshListIfChanged(int paramIndex, string subfolder, string inputName, ref string lastFiles)
    {
        string current = GetFileList(subfolder);
        if (current == lastFiles) return;

        lastFiles = current;
        string[] fileNames = current.Length > 0 ? current.Split(',') : Array.Empty<string>();
        SetValues(inputName, fileNames);

        OnPingDocument()?.ScheduleSolution(1, _ =>
        {
            foreach (var source in Params.Input[paramIndex].Sources)
                (source.Attributes?.GetTopLevel?.DocObject as IGH_ActiveObject)?.ExpireSolution(false);
            ExpireSolution(true);
        });
    }

    private static string Assemble(string preamble, string schema, string additional)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(preamble))
            parts.Add(preamble.Trim());

        if (!string.IsNullOrWhiteSpace(schema))
        {
            parts.Add("When your response carries the work itself — a definition, a patch, or a script — it must be valid JSON that conforms exactly to the following schema. A response that only answers a question or talks to the user carries no JSON and is not measured against this schema:");
            parts.Add(schema.Trim());
        }

        if (!string.IsNullOrWhiteSpace(additional))
            parts.Add(additional.Trim());

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

}
