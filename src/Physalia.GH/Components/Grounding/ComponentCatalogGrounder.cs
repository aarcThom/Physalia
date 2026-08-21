// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
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

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetBoolean("IncludeLegacy", _includeLegacy);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _includeLegacy = reader.ItemExists("IncludeLegacy") && reader.GetBoolean("IncludeLegacy");
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
