// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Physalia.GH.Helpers;

/// <summary>
/// An information container describing the objects, hooks, and build state of a
/// Grasshopper document. Works for both inner cluster documents and the main canvas.
///
/// Two construction paths both produce a state that
/// <see cref="GHSystemHelpers.DescribeDocumentStructure"/> can work on:
/// <list type="bullet">
///   <item><see cref="FromDocument"/> — scans a live document; suitable for description and querying.</item>
///   <item>Returned by <see cref="GHSystemHelpers.PlaceObjects"/> — built incrementally during a
///   cluster build; also carries wiring lookup tables and accumulated errors.</item>
/// </list>
/// </summary>
public class GhDocState
{
    // PROPERTIES ==================================================================

    /// <summary>
    /// Gets all non-hook document objects (components and standalone params).
    /// Hooks are stored separately in <see cref="InputHooks"/> and <see cref="OutputHooks"/>.
    /// </summary>
    public IReadOnlyList<IGH_DocumentObject> Objects { get; }

    /// <summary>
    /// Gets the cluster input hooks present in the document.
    /// Empty when describing a non-cluster document.
    /// </summary>
    public IReadOnlyList<IGH_Param> InputHooks { get; }

    /// <summary>
    /// Gets the cluster output hooks present in the document.
    /// Empty when describing a non-cluster document.
    /// </summary>
    public IReadOnlyList<IGH_Param> OutputHooks { get; }

    /// <summary>
    /// Gets build or wiring errors accumulated during a build phase.
    /// Empty when the state was created via <see cref="FromDocument"/>.
    /// </summary>
    public List<string> Errors { get; } = new();

    // Internal — only used by GHSystemHelpers build methods for wiring.
    internal Dictionary<string, IGH_Component> ById { get; }
    internal Dictionary<string, IGH_Param> ParamById { get; }
    internal Dictionary<Guid, Guid> PanelTargets { get; }

    // CONSTRUCTOR =================================================================

    // Internal: called by GHSystemHelpers.PlaceObjects after all objects are placed.
    internal GhDocState(
        IReadOnlyList<IGH_DocumentObject> objects,
        IGH_Param[] inputHooks,
        IGH_Param[] outputHooks,
        Dictionary<string, IGH_Component> byId,
        Dictionary<string, IGH_Param> paramById,
        Dictionary<Guid, Guid> panelTargets)
    {
        Objects = objects;
        InputHooks = inputHooks;
        OutputHooks = outputHooks;
        ById = byId;
        ParamById = paramById;
        PanelTargets = panelTargets;
    }

    // FACTORY =====================================================================

    /// <summary>
    /// Creates a <see cref="GhDocState"/> by scanning a live <see cref="GH_Document"/>.
    /// Suitable for describing any document — inner cluster or main canvas.
    /// <see cref="ById"/>, <see cref="ParamById"/>, and <see cref="PanelTargets"/> are empty
    /// because no LLM-assigned IDs exist in a scanned document.
    /// </summary>
    /// <param name="doc">The GH document to scan.</param>
    /// <returns>A populated <see cref="GhDocState"/>.</returns>
    public static GhDocState FromDocument(GH_Document doc)
    {
        IGH_Param[] inputHooks = doc.ClusterInputHooks() ?? Array.Empty<GH_ClusterInputHook>();
        IGH_Param[] outputHooks = doc.ClusterOutputHooks() ?? Array.Empty<GH_ClusterOutputHook>();

        // Exclude hooks from the object list — they are exposed separately.
        var hookGuids = new HashSet<Guid>(
            inputHooks.Concat(outputHooks).Select(h => h.InstanceGuid));

        var objects = doc.Objects
            .Where(o => !hookGuids.Contains(o.InstanceGuid))
            .ToList<IGH_DocumentObject>();

        return new GhDocState(
            objects,
            inputHooks,
            outputHooks,
            new Dictionary<string, IGH_Component>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IGH_Param>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<Guid, Guid>());
    }
}
