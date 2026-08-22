// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Components;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Reads the live Grasshopper component server and outputs a snapshot of the installed,
/// non-obsolete components — their names, type GUIDs, and category placement. Downstream, the
/// Component Resolver uses it to map an LLM's component names to real installed components, and the
/// System Prompt can fold the available names into the system prompt. Has no inputs; right-click to
/// refresh after installing plug-ins.
/// </summary>
public class ComponentCatalogGrounder : PhyBase
{
    private ComponentCatalog? _catalog;

    // Legacy (hidden-exposure) components are kept registered by Grasshopper for backward
    // compatibility but do not appear in the ribbon — e.g. an old colour "Multiplication" that
    // collides by name with the current one and confuses the model. Excluded by default; a
    // right-click toggle brings them back. Persisted, since it is configuration. Obscure-exposure
    // components (e.g. Mass Multiplication) are demoted but genuinely useful, and are kept.
    private bool _includeLegacy;

    // Which tabs and panels of this catalog the model is told about. Null = the never-configured
    // default (every leaf holding native components; plug-in tabs stay listed but unchecked until
    // opted in). Edited from the chat window's grounding page, which reaches it through the
    // Conversation Log; it lives HERE so it travels with the component — copy this grounder into
    // another harness, or ship it inside a preset, and the selection comes along.
    private GroundingSelection? _selection;

    // Whether every included component carries its typed input/output signature into the prompt,
    // rather than only the curated common set (the hybrid default). For models without tool calling
    // but with large contexts. Same ownership story as _selection.
    private bool _exposeSignatures;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentCatalogGrounder"/> class.
    /// </summary>
    public ComponentCatalogGrounder()
        : base("Component Catalog", "Component Catalog", "Takes stock of every Grasshopper component installed on this machine. A Component Resolver matches generated names against it, and the model reads it to know what it is allowed to use. Right-click to take stock again.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D2F8B41A-6C35-4E29-A1B7-3F0E5D9C82B6");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs: the catalog is read from the live component server.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ComponentCatalog(), "Component Catalog", "Cat", "Every component installed here, by name and type id. Wire into a Component Resolver, a Component Search tool, or a Conversation Log's Grounding input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Refresh catalog", OnRefresh);
        Menu_AppendItem(
            menu,
            "Include legacy (hidden) components",
            OnToggleLegacy,
            enabled: true,
            @checked: _includeLegacy);
    }

    /// <summary>
    /// Gets the selection of tabs and panels the model is told about, or null for the
    /// never-configured default (native components only).
    /// </summary>
    public GroundingSelection? Selection => _selection;

    /// <summary>
    /// Gets a value indicating whether every included component carries its typed signature into the
    /// prompt, rather than only the curated common set.
    /// </summary>
    public bool ExposeSignatures => _exposeSignatures;

    /// <summary>
    /// Sets which tabs and panels the model is told about. Called from the chat window (through the
    /// Conversation Log) on the UI thread; does not re-solve, because the catalog on the wire is
    /// unaffected — the Conversation Log reads this selection live on its own next solve, which the
    /// caller triggers.
    /// </summary>
    /// <param name="selection">The new selection, or null to return to the default.</param>
    public void SetSelection(GroundingSelection? selection) => _selection = selection;

    /// <summary>
    /// Sets whether typed signatures are folded in for every included component. Called from the chat
    /// window (through the Conversation Log) on the UI thread; see <see cref="SetSelection"/> for why
    /// it does not re-solve.
    /// </summary>
    /// <param name="on">True to fold signatures in for every included component.</param>
    public void SetExposeSignatures(bool on) => _exposeSignatures = on;

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetBoolean("IncludeLegacy", _includeLegacy);
        writer.SetBoolean("ExposeSignatures", _exposeSignatures);
        SettingArchive.WriteOptionalLeaves(writer, "GroundingSelection", _selection?.Leaves);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _includeLegacy = reader.ItemExists("IncludeLegacy") && reader.GetBoolean("IncludeLegacy");
        _exposeSignatures = reader.ItemExists("ExposeSignatures") && reader.GetBoolean("ExposeSignatures");
        _selection = SettingArchive.ReadOptionalLeaves(reader, "GroundingSelection") is { } leaves
            ? GroundingSelection.FromLeaves(leaves)
            : null;
        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        _catalog ??= ComponentCatalogProvider.BuildFromServer(_includeLegacy);
        DA.SetData(0, new GH_ComponentCatalog(_catalog));
    }

    private void OnRefresh(object? sender, EventArgs e)
    {
        _catalog = null;
        ExpireSolution(true);
    }

    private void OnToggleLegacy(object? sender, EventArgs e)
    {
        _includeLegacy = !_includeLegacy;
        _catalog = null;
        ExpireSolution(true);
    }
}
