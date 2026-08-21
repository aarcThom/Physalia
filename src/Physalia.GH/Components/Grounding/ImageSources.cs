// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Goo;
using Physalia.GH.Panels;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Gathers local or clipboard images and outputs them as image-source goo, each carrying
/// raw bytes, MIME type, and a user-entered alias. Right-click → Manage Images opens an
/// Eto panel to add, alias, preview, and remove images. Has no inputs.
/// </summary>
public class ImageSources : PhyBase
{
    private readonly List<ImageEntry> _entries = new();
    private readonly List<string> _loadWarnings = new();
    private ManageImagesDialog? _dialog;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageSources"/> class.
    /// </summary>
    public ImageSources()
        : base("Image Sources", "Image Sources", "Collects pictures from disk or the clipboard and hands them on for a model that can see. Right-click to add, remove or rename them.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B7E91D34-2C5A-4F18-A0D6-3E84C9B17F52");

    /// <summary>
    /// Gets the current image entries (source of truth; the panel edits a copy and pushes back).
    /// </summary>
    internal IReadOnlyList<ImageEntry> Entries => _entries;

    /// <summary>
    /// Maps a file extension to its image MIME type, or null if unsupported.
    /// </summary>
    /// <param name="path">The file path or name to inspect.</param>
    /// <returns>The MIME type, or null for an unsupported extension.</returns>
    internal static string? MimeFromExtension(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => null,
        };
    }

    /// <summary>
    /// Collapses whitespace runs to single hyphens so an alias is one token, referenceable
    /// in a prompt as "/&lt;alias&gt;" without ambiguity. Trims surrounding whitespace.
    /// </summary>
    /// <param name="alias">The raw alias.</param>
    /// <returns>The whitespace-free alias.</returns>
    internal static string SanitizeAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(alias.Trim(), @"\s+", "-");
    }

    /// <summary>
    /// Produces an alias not already taken (case-insensitive) by appending "-2", "-3", … on collision.
    /// </summary>
    /// <param name="desired">The desired base alias.</param>
    /// <param name="existing">The entries to test against.</param>
    /// <returns>A unique alias.</returns>
    internal static string UniqueAlias(string desired, IEnumerable<ImageEntry> existing)
    {
        if (string.IsNullOrWhiteSpace(desired))
        {
            desired = "image";
        }

        var taken = new HashSet<string>(existing.Select(e => e.Alias), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(desired))
        {
            return desired;
        }

        for (int i = 2; ; i++)
        {
            string candidate = $"{desired}-{i}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Replaces the component's image entries and triggers a re-solve. Called by the panel on every edit.
    /// </summary>
    /// <param name="entries">The new set of entries.</param>
    internal void ReplaceEntries(IEnumerable<ImageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries.Clear();
        _entries.AddRange(entries);
        ExpireSolution(true);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ImageSource(), "Image Sources", "Img", "One entry per picture, carrying the image itself and the short name you refer to it by in a prompt.", GH_ParamAccess.list);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Manage Images", OnManageImages);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Surface any warnings deferred from document load (missing files), then clear them.
        foreach (string warning in _loadWarnings)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }

        _loadWarnings.Clear();

        var goos = new List<GH_ImageSource>(_entries.Count);
        foreach (ImageEntry entry in _entries)
        {
            if (entry.Data.Length == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Image \"{entry.Alias}\" has no data and was skipped.");
                continue;
            }

            goos.Add(new GH_ImageSource(new ImageResource(entry.Alias, new InlineImage(entry.Data, entry.MimeType))));
        }

        DA.SetDataList(0, goos);
    }

    /// <summary>
    /// Persists only file-backed entries (path + alias). Clipboard images have no path
    /// and cannot be re-sourced, so they are not written.
    /// </summary>
    /// <param name="writer">The writer to serialize into.</param>
    /// <returns>true on success.</returns>
    public override bool Write(GH_IWriter writer)
    {
        var persisted = _entries.Where(e => !string.IsNullOrEmpty(e.FilePath)).ToList();
        writer.SetInt32("ImageCount", persisted.Count);
        for (int i = 0; i < persisted.Count; i++)
        {
            writer.SetString($"ImagePath_{i}", persisted[i].FilePath);
            writer.SetString($"ImageAlias_{i}", persisted[i].Alias);
        }

        return base.Write(writer);
    }

    /// <summary>
    /// Restores file-backed entries by re-reading their bytes from disk. Missing files are
    /// skipped and reported as warnings on the next solve.
    /// </summary>
    /// <param name="reader">The reader to deserialize from.</param>
    /// <returns>true on success.</returns>
    public override bool Read(GH_IReader reader)
    {
        _entries.Clear();
        _loadWarnings.Clear();

        if (reader.ItemExists("ImageCount"))
        {
            int count = reader.GetInt32("ImageCount");
            for (int i = 0; i < count; i++)
            {
                string? path = reader.ItemExists($"ImagePath_{i}") ? reader.GetString($"ImagePath_{i}") : null;
                string alias = reader.ItemExists($"ImageAlias_{i}") ? reader.GetString($"ImageAlias_{i}") : $"image-{i}";

                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (!File.Exists(path))
                {
                    _loadWarnings.Add($"Image file not found, skipped: {path}");
                    continue;
                }

                string? mime = MimeFromExtension(path);
                if (mime is null)
                {
                    _loadWarnings.Add($"Unsupported image type, skipped: {path}");
                    continue;
                }

                try
                {
                    _entries.Add(new ImageEntry
                    {
                        FilePath = path,
                        Alias = alias,
                        Data = File.ReadAllBytes(path),
                        MimeType = mime,
                    });
                }
                catch (Exception ex)
                {
                    _loadWarnings.Add($"Failed to read image {path}: {ex.Message}");
                }
            }
        }

        return base.Read(reader);
    }

    private void OnManageImages(object? sender, EventArgs e)
    {
        if (_dialog is { } existing)
        {
            existing.BringToFront();
            existing.Focus();
            return;
        }

        _dialog = new ManageImagesDialog(this);
        _dialog.Closed += (_, _) => _dialog = null;
        _dialog.Show();
    }
}
