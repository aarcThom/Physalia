// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Grounding.Clusters;
using Physalia.Core.Grounding.Components;
using Physalia.Core.Signals;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves the component names in a generated GhJSON graph (arriving as the consumed signal's
/// payload) against a live component catalog from the Library component, rewriting each name to
/// a real installed component and stamping its type GUID so placement is exact. A fully resolved
/// graph routes forward on the Success Signal; names that cannot be matched confidently route a
/// description back on the Fail Signal so the model can correct them. With no catalog wired the
/// graph passes through unchanged.
/// </summary>
public class Resolver : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Resolver"/> class.
    /// </summary>
    public Resolver()
        : base("Resolver", "Rslv", "Resolves generated component names to real installed components and stamps their type GUIDs; unresolved names route back as feedback.", "Deterministic Gates")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("E3A9C52B-7D46-4F3A-B2C8-4A1F6E0D93C7");

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        int index = pManager.AddParameter(new Param_ComponentCatalog(), "Component Catalog", "Cat", "Installed-component catalog from the Library component.", GH_ParamAccess.item);
        pManager[index].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>The generated GhJSON arrives as the consumed signal's payload (Auditor's Success Signal).</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — no settle pass needed; all work is in ReadSolve.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: resolution has no side effects to push before reading.
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        GH_ComponentCatalog? catalogGoo = null;
        da.GetData(0, ref catalogGoo);
        ComponentCatalog catalog = catalogGoo?.Value ?? new ComponentCatalog(Array.Empty<CatalogEntry>());

        // Clusters resolve against every file in Files/CLUSTERS (not just the chat-selected ones), so a
        // hand-authored graph referencing any installed cluster still places.
        ClusterCatalog clusters = ClusterCatalogProvider.GetCatalog();

        if (catalog.Count == 0 && clusters.Count == 0)
        {
            // Nothing to resolve against — pass the graph through unchanged.
            return RoutingResult.Ok(data);
        }

        try
        {
            (string resolved, IReadOnlyList<string> unresolved) = GhJsonBridge.ResolveComponentNames(data, catalog, clusters);

            return unresolved.Count == 0
                ? RoutingResult.Ok(resolved)
                : RoutingResult.Fail(BuildFeedback(unresolved), $"{unresolved.Count} component name(s) could not be resolved.", GH_RuntimeMessageLevel.Warning);
        }
        catch (Exception ex)
        {
            return RoutingResult.Fail($"Could not resolve component names: {ex.Message}", ex.Message, GH_RuntimeMessageLevel.Error);
        }
    }

    private static string BuildFeedback(IReadOnlyList<string> unresolved)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Some components you named do not match any installed Grasshopper component. Replace them with real component names and resubmit.");
        sb.AppendLine();
        sb.AppendLine("Unresolved component names:");
        foreach (string name in unresolved)
        {
            sb.AppendLine($"  - {name}");
        }

        return sb.ToString().TrimEnd();
    }
}
