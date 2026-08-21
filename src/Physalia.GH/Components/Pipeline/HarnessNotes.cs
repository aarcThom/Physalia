// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.GH.Attributes;

namespace Physalia.GH.Components;

/// <summary>
/// A Physalia-themed notes panel that lives inside a harness and says what that harness is for.
///
/// <para>Two audiences. On the canvas it is documentation for whoever opens the harness — the same job
/// a Grasshopper panel does, in the harness family's own livery. And when the harness is saved as a
/// preset, this text becomes the preset's <b>description</b> in the chat window's gallery: a
/// Grasshopper file carries no description of its own, so a component holding one is how a preset
/// explains itself before it is placed.</para>
///
/// <para>Notes are read out of a preset file WITHOUT loading it — see
/// <see cref="Harness.PresetLibrary.ReadDescription"/> — so <see cref="TypeGuid"/> and
/// <see cref="NotesKey"/> are part of the archive contract. Changing either orphans the descriptions
/// of every preset already saved.</para>
/// </summary>
public class HarnessNotes : PhyBase
{
    /// <summary>
    /// The archive key the notes text is written under. Part of the preset-description contract; see
    /// the type remarks.
    /// </summary>
    internal const string NotesKey = "HarnessNotes";

    /// <summary>
    /// This component's type id. Part of the preset-description contract; see the type remarks.
    /// </summary>
    internal static readonly Guid TypeGuid = new Guid("5C1D9E38-7A24-4B6F-8E03-2D97F4A1C6B5");

    // Archive key for the panel's width, so a widened panel stays widened.
    private const string WidthKey = "HarnessNotesWidth";

    private string _notes = string.Empty;
    private float _width;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessNotes"/> class.
    /// </summary>
    public HarnessNotes()
        : base(
            "Harness Notes",
            "Notes",
            "A note on what this harness is for. Double-click to write in it. Save the harness as a preset and this text becomes the preset's description in the chat window's gallery.",
            "Pipeline")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => TypeGuid;

    /// <summary>
    /// Gets the notes text, or an empty string when nothing has been written yet.
    /// </summary>
    public string Notes => _notes;

    /// <summary>
    /// Gets or sets the panel's width in canvas units. Clamped to a legible minimum.
    /// </summary>
    internal float Width
    {
        get => _width < HarnessNotesAttrib.MinWidth ? HarnessNotesAttrib.DefaultWidth : _width;
        set => _width = Math.Max(HarnessNotesAttrib.MinWidth, value);
    }

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new HarnessNotesAttrib(this);
    }

    /// <summary>
    /// Opens a multi-line prompt on the notes text and stores what comes back.
    ///
    /// <para>A dialog rather than an in-place editor: an editable text box on the canvas is a
    /// WinForms surface with its own focus and lifetime problems (the reason the old Prompter panel was
    /// awkward on Mac), and notes are written once and read often. Shows a dialog, so it must run on
    /// the UI thread.</para>
    /// </summary>
    public void EditNotes()
    {
        if (!Rhino.UI.Dialogs.ShowEditBox(
                "Harness Notes",
                "What is this harness for? This text becomes the preset's description.",
                _notes,
                true,
                out string edited))
        {
            return; // cancelled
        }

        string text = edited ?? string.Empty;
        if (string.Equals(text, _notes, StringComparison.Ordinal))
        {
            return;
        }

        _notes = text;

        // Layout depends on the wrapped text's height, so the panel has to be re-measured. No solve is
        // needed — nothing downstream depends on the notes.
        Attributes?.ExpireLayout();
        OnPingDocument()?.DestroyAttributeCache();
        Grasshopper.Instances.ActiveCanvas?.Refresh();
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Edit Notes…", (_, _) => EditNotes());
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString(NotesKey, _notes);
        writer.SetSingle(WidthKey, Width);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        string notes = string.Empty;
        if (reader.TryGetString(NotesKey, ref notes))
        {
            _notes = notes ?? string.Empty;
        }

        float width = 0f;
        if (reader.TryGetSingle(WidthKey, ref width))
        {
            _width = width;
        }

        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // None — the panel is documentation, not a pipeline stage.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // None.
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Nothing to compute.
    }
}
