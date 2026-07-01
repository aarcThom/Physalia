// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Physalia.Core.Grounding.Components;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Reads the live Grasshopper component server and outputs a snapshot of the installed,
/// non-obsolete components — their names, type GUIDs, and category placement. Downstream, the
/// Resolver uses it to map an LLM's component names to real installed components, and the
/// Composer can fold the available names into the system prompt. Has no inputs; right-click to
/// refresh after installing plug-ins.
/// </summary>
public class Library : PhyBase
{
    private ComponentCatalog? _catalog;

    // Legacy (hidden-exposure) components are kept registered by Grasshopper for backward
    // compatibility but do not appear in the ribbon — e.g. an old colour "Multiplication" that
    // collides by name with the current one and confuses the model. Excluded by default; a
    // right-click toggle brings them back. Persisted, since it is configuration.
    private bool _includeLegacy;

    /// <summary>
    /// Initializes a new instance of the <see cref="Library"/> class.
    /// </summary>
    public Library()
        : base("Library", "Lib", "Snapshots the installed Grasshopper components (names and type GUIDs) for resolving and grounding generated graphs. Right-click to refresh.", "Resources")
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
        pManager.AddParameter(new Param_ComponentCatalog(), "Component Catalog", "Cat", "Snapshot of the installed Grasshopper components.", GH_ParamAccess.item);
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
        _catalog ??= BuildCatalog(_includeLegacy);
        DA.SetData(0, new GH_ComponentCatalog(_catalog));
    }

    /// <summary>
    /// Enumerates the component server once and builds the catalog, skipping obsolete entries,
    /// anything without a usable display name, and — unless <paramref name="includeLegacy"/> is set —
    /// hidden-exposure (legacy/back-compat) components that are kept registered but never shown in the
    /// ribbon. Native (core-library) membership is recorded so the matcher can prefer stock components.
    /// </summary>
    /// <param name="includeLegacy">True to keep hidden-exposure legacy components in the catalog.</param>
    /// <returns>The built catalog.</returns>
    private static ComponentCatalog BuildCatalog(bool includeLegacy)
    {
        var server = Instances.ComponentServer;
        var coreLibs = new HashSet<Guid>(server.Libraries.Where(l => l.IsCoreLibrary).Select(l => l.Id));

        var entries = new List<CatalogEntry>();
        foreach (IGH_ObjectProxy proxy in server.ObjectProxies)
        {
            if (proxy?.Desc is null || proxy.Obsolete)
            {
                continue;
            }

            // Hidden exposure marks a component Grasshopper keeps for back-compat but omits from the
            // ribbon — the "old" twin that collides by name with the current component. Skip by default.
            if (!includeLegacy && proxy.Exposure == GH_Exposure.hidden)
            {
                continue;
            }

            string name = proxy.Desc.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf("OBSOLETE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            entries.Add(new CatalogEntry(
                name,
                proxy.Guid,
                proxy.Desc.Category ?? string.Empty,
                proxy.Desc.SubCategory ?? string.Empty,
                proxy.Desc.NickName ?? string.Empty,
                coreLibs.Contains(proxy.LibraryGuid)));
        }

        return new ComponentCatalog(entries);
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
