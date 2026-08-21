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
using Physalia.Core.Validation;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves the component names in a generated GhJSON graph (arriving as the consumed signal's
/// payload) against a live component catalog from the Component Catalog component, rewriting each name to
/// a real installed component and stamping its type GUID so placement is exact. A fully resolved
/// graph routes forward on the Success Signal; names that cannot be matched confidently route a
/// description back on the Fail Signal so the model can correct them. With no catalog wired the
/// graph passes through unchanged. A ghpatch payload has only its ADDED components resolved —
/// every other patch operation addresses components already on the canvas and passes through
/// untouched.
/// </summary>
public class ComponentResolver : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentResolver"/> class.
    /// </summary>
    public ComponentResolver()
        : base("Component Resolver", "Component Resolver", "Matches the component names in a generated definition to components actually installed here and stamps in their type ids. A name matching nothing goes back to the model rather than failing on the canvas.", "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("E3A9C52B-7D46-4F3A-B2C8-4A1F6E0D93C7");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The definition whose component names need matching. Wire a validator's Success Signal.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "The same definition, with every component now identified by the type id of a real installed one. Wire into a Component Transmitter.";

    /// <inheritdoc/>
    protected override string FailSignalDescription =>
        "The names that match nothing on this machine, so the model can choose components that exist here. Wire into a Feedback.";

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        int index = pManager.AddParameter(new Param_ComponentCatalog(), "Component Catalog", "Cat", "The components installed on this machine, to match names against. Wire a Component Catalog.", GH_ParamAccess.item);
        pManager[index].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>The generated GhJSON arrives as the consumed signal's payload (Schema Validator's Success Signal).</remarks>
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
            // A ghpatch must not take the full-document path: parsing it as a GhJsonDocument
            // drops its kind/patch fields, re-serialising an empty document that downstream
            // placement rejects. Resolve only the components the patch ADDS instead.
            (string resolved, IReadOnlyList<string> unresolved) = GhPatchDetector.IsGhPatch(data)
                ? GhJsonBridge.ResolvePatchComponentNames(data, catalog, clusters)
                : GhJsonBridge.ResolveComponentNames(data, catalog, clusters);

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
