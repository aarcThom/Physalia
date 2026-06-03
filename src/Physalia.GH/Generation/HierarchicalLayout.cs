// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Physalia.GH.Generation;

/// <summary>
/// Applies a Sugiyama-style hierarchical layout to all objects in a Grasshopper document.
/// Nodes are assigned to layers by longest-path from sources and positioned left-to-right;
/// within each layer nodes are sorted top-to-bottom by the barycenter heuristic to reduce
/// edge crossings. Annotation panels are placed in a separate column to the right of the
/// main graph.
/// </summary>
public static class HierarchicalLayout
{
    // LAYOUT CONSTANTS =============================================================
    private const float LayerSpacing = 220f;
    private const float NodeSpacing = 100f;
    private const float PanelSpacing = 100f;
    private const float OriginX = 50f;
    private const float OriginY = 50f;
    private const float PanelColumnGap = 80f;
    private const float PanelOffset = 130f; // horizontal offset from target pivot for annotated panels

    // PUBLIC API ===================================================================

    /// <summary>
    /// Repositions all objects in the given document using a left-to-right hierarchical layout.
    /// Nodes are layered by data-flow depth. Annotated panels are placed beside their target
    /// component; unannotated panels go in a right column.
    /// </summary>
    /// <param name="doc">The document whose objects will be repositioned.</param>
    /// <param name="panelTargets">
    /// Optional map from panel InstanceGuid to the InstanceGuid of the component it annotates.
    /// Panels present in this map are placed to the right of their target at the same Y position.
    /// </param>
    public static void Apply(GH_Document doc, Dictionary<Guid, Guid>? panelTargets = null)
    {
        var allObjs = doc.Objects.OfType<IGH_DocumentObject>().ToList();
        if (allObjs.Count == 0)
            return;

        // Separate annotation panels — they go in a right column, outside the flow graph.
        var panels = allObjs.OfType<GH_Panel>().Cast<IGH_DocumentObject>().ToList();
        var mainObjs = allObjs.Except(panels).ToList();

        // Map each IGH_Param InstanceGuid to the document object that owns it.
        var paramToOwner = BuildParamOwnerMap(mainObjs);

        // Build upstream adjacency: node guid → set of upstream node guids.
        var upstream = BuildUpstreamMap(mainObjs, paramToOwner);

        // Assign layers via longest-path from sources (recursive, cycle-safe).
        var layerMap = new Dictionary<Guid, int>();
        var inProgress = new HashSet<Guid>();
        foreach (var obj in mainObjs)
            AssignLayer(obj.InstanceGuid, upstream, layerMap, inProgress);

        // Reassign standalone params that have no incoming wires to (min recipient layer - 1)
        // so they appear immediately left of the component they feed.
        foreach (var obj in mainObjs)
        {
            if (upstream[obj.InstanceGuid].Count == 0 && obj is IGH_Param param)
            {
                var recipientLayers = GetRecipientLayers(param, paramToOwner, layerMap);
                if (recipientLayers.Count > 0)
                    layerMap[obj.InstanceGuid] = recipientLayers.Min() - 1;
            }
        }

        // Normalise so the minimum layer is 0.
        int minLayer = mainObjs.Count > 0
            ? mainObjs.Min(o => layerMap.GetValueOrDefault(o.InstanceGuid, 0))
            : 0;
        foreach (var obj in mainObjs)
            layerMap[obj.InstanceGuid] = layerMap.GetValueOrDefault(obj.InstanceGuid, 0) - minLayer;

        // Group objects into per-layer lists.
        int maxLayer = mainObjs.Count > 0
            ? mainObjs.Max(o => layerMap.GetValueOrDefault(o.InstanceGuid, 0))
            : 0;
        var byLayer = Enumerable.Range(0, maxLayer + 1)
            .Select(l => mainObjs.Where(o => layerMap.GetValueOrDefault(o.InstanceGuid, 0) == l).ToList())
            .ToList();

        // Sort within each layer using the barycenter heuristic to reduce edge crossings.
        SortByBarycenter(byLayer, upstream);

        // Assign pivot positions left-to-right by layer, top-to-bottom within each layer.
        for (int layer = 0; layer <= maxLayer; layer++)
        {
            float x = OriginX + layer * LayerSpacing;
            float y = OriginY;
            foreach (var obj in byLayer[layer])
            {
                if (obj.Attributes == null)
                    obj.CreateAttributes();
                obj.Attributes.Pivot = new PointF(x, y);
                y += NodeSpacing;
            }
        }

        // Build a quick pivot lookup so panels can find their target's position.
        var pivotByGuid = mainObjs.ToDictionary(o => o.InstanceGuid, o => o.Attributes.Pivot);

        // Place annotated panels beside their target; fall unannotated ones into a right column.
        float annotationX = OriginX + (maxLayer + 1) * LayerSpacing + PanelColumnGap;
        float annotationY = OriginY;
        foreach (var panel in panels)
        {
            if (panel.Attributes == null)
                panel.CreateAttributes();

            if (panelTargets != null
                && panelTargets.TryGetValue(panel.InstanceGuid, out Guid targetGuid)
                && pivotByGuid.TryGetValue(targetGuid, out PointF targetPivot))
            {
                // Place the panel to the right of its target at the same Y.
                panel.Attributes.Pivot = new PointF(targetPivot.X + PanelOffset, targetPivot.Y);
            }
            else
            {
                panel.Attributes.Pivot = new PointF(annotationX, annotationY);
                annotationY += PanelSpacing;
            }
        }
    }

    // PRIVATE HELPERS ==============================================================

    // Maps every IGH_Param InstanceGuid → the document object that owns it.
    // Component input/output params map to their parent component;
    // standalone params (Number, Boolean, etc.) map to themselves.
    private static Dictionary<Guid, IGH_DocumentObject> BuildParamOwnerMap(
        List<IGH_DocumentObject> objs)
    {
        var map = new Dictionary<Guid, IGH_DocumentObject>();
        foreach (var obj in objs)
        {
            if (obj is IGH_Component comp)
            {
                foreach (var p in comp.Params.Input.Concat(comp.Params.Output))
                    map[p.InstanceGuid] = comp;
            }
            else if (obj is IGH_Param param)
            {
                map[param.InstanceGuid] = param;
            }
        }

        return map;
    }

    // Builds upstream adjacency: each node guid → set of upstream node guids
    // derived from the Sources wired into every input param.
    private static Dictionary<Guid, HashSet<Guid>> BuildUpstreamMap(
        List<IGH_DocumentObject> objs,
        Dictionary<Guid, IGH_DocumentObject> paramToOwner)
    {
        var map = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var obj in objs)
            map[obj.InstanceGuid] = new HashSet<Guid>();

        foreach (var obj in objs)
        {
            IEnumerable<IGH_Param> inputParams = obj switch
            {
                IGH_Component comp => comp.Params.Input,
                IGH_Param param => new IGH_Param[] { param },
                _ => Array.Empty<IGH_Param>()
            };

            foreach (var inputParam in inputParams)
            {
                foreach (var source in inputParam.Sources)
                {
                    if (!paramToOwner.TryGetValue(source.InstanceGuid, out var sourceOwner))
                        continue;
                    if (sourceOwner.InstanceGuid != obj.InstanceGuid)
                        map[obj.InstanceGuid].Add(sourceOwner.InstanceGuid);
                }
            }
        }

        return map;
    }

    // Assigns a layer number via recursive longest-path from sources.
    // inProgress guards against cycles (GH graphs should be acyclic, but just in case).
    private static int AssignLayer(
        Guid id,
        Dictionary<Guid, HashSet<Guid>> upstream,
        Dictionary<Guid, int> layers,
        HashSet<Guid> inProgress)
    {
        if (layers.TryGetValue(id, out int existing))
            return existing;

        if (!inProgress.Add(id))
            return 0; // cycle — break it

        int layer = 0;
        if (upstream.TryGetValue(id, out var upSet))
        {
            foreach (var upId in upSet)
                layer = Math.Max(layer, AssignLayer(upId, upstream, layers, inProgress) + 1);
        }

        inProgress.Remove(id);
        layers[id] = layer;
        return layer;
    }

    // Returns the layer indices of all document objects that receive data from param.
    private static List<int> GetRecipientLayers(
        IGH_Param param,
        Dictionary<Guid, IGH_DocumentObject> paramToOwner,
        Dictionary<Guid, int> layerMap)
    {
        var result = new List<int>();
        foreach (var recipient in param.Recipients)
        {
            if (paramToOwner.TryGetValue(recipient.InstanceGuid, out var owner)
                && owner.InstanceGuid != param.InstanceGuid
                && layerMap.TryGetValue(owner.InstanceGuid, out int layer))
            {
                result.Add(layer);
            }
        }

        return result;
    }

    // Sorts nodes within each layer by their average upstream-neighbour position
    // (barycenter heuristic, three forward passes for better convergence).
    private static void SortByBarycenter(
        List<List<IGH_DocumentObject>> byLayer,
        Dictionary<Guid, HashSet<Guid>> upstream)
    {
        var position = new Dictionary<Guid, float>();

        for (int l = 0; l < byLayer.Count; l++)
            for (int i = 0; i < byLayer[l].Count; i++)
                position[byLayer[l][i].InstanceGuid] = i;

        for (int pass = 0; pass < 3; pass++)
        {
            for (int l = 1; l < byLayer.Count; l++)
            {
                foreach (var obj in byLayer[l])
                {
                    var upPos = upstream[obj.InstanceGuid]
                        .Where(uid => position.ContainsKey(uid))
                        .Select(uid => position[uid])
                        .ToList();

                    if (upPos.Count > 0)
                        position[obj.InstanceGuid] = upPos.Average();
                }

                byLayer[l] = byLayer[l]
                    .OrderBy(o => position[o.InstanceGuid])
                    .ToList();

                for (int i = 0; i < byLayer[l].Count; i++)
                    position[byLayer[l][i].InstanceGuid] = i;
            }
        }
    }
}
