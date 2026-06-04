// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Physalia.GH.Generation;

/// <summary>
/// Computes a Sugiyama-style hierarchical layout for a <see cref="PhySchemaDocument"/>.
/// Components are assigned to layers by longest-path from sources and positioned
/// left-to-right by data flow; within each layer they are ordered top-to-bottom by the
/// barycenter heuristic to reduce edge crossings. The result is a map from each
/// component's integer id to its canvas pivot — the document itself is never mutated.
/// </summary>
public static class HierarchicalLayout
{
    // LAYOUT CONSTANTS =============================================================
    private const float LayerSpacing = 300f;
    private const float NodeSpacing = 100f;
    private const float OriginX = 50f;
    private const float OriginY = 50f;

    // PUBLIC API ===================================================================

    /// <summary>
    /// Computes a left-to-right hierarchical pivot for every component in the schema.
    /// Components are layered by data-flow depth; pure source components are pulled
    /// rightward to sit immediately left of their earliest consumer.
    /// </summary>
    /// <param name="schema">The PhySchema document to lay out.</param>
    /// <returns>A map from component id to its computed canvas pivot.</returns>
    public static IReadOnlyDictionary<int, PointF> ComputePositions(PhySchemaDocument schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var result = new Dictionary<int, PointF>();
        if (schema.Components is null || schema.Components.Count == 0)
            return result;

        // Valid node id set — connection endpoints referencing anything else are ignored.
        var nodes = new HashSet<int>();
        foreach (var component in schema.Components)
            nodes.Add(component.Id);

        // Build upstream / downstream adjacency from the connection id-graph.
        var upstream = nodes.ToDictionary(id => id, _ => new HashSet<int>());
        var downstream = nodes.ToDictionary(id => id, _ => new HashSet<int>());
        if (schema.Connections is not null)
        {
            foreach (var connection in schema.Connections)
            {
                int from = connection.From.Id;
                int to = connection.To.Id;
                if (from == to || !nodes.Contains(from) || !nodes.Contains(to))
                    continue;
                upstream[to].Add(from);
                downstream[from].Add(to);
            }
        }

        // Assign layers via longest-path from sources (recursive, cycle-safe).
        var layerMap = new Dictionary<int, int>();
        var inProgress = new HashSet<int>();
        foreach (int id in nodes)
            AssignLayer(id, upstream, layerMap, inProgress);

        // Pull pure source nodes rightward to sit immediately left of their earliest
        // consumer, so sliders / value lists hug the component they feed.
        foreach (int id in nodes)
        {
            if (upstream[id].Count == 0 && downstream[id].Count > 0)
            {
                int minConsumerLayer = downstream[id].Min(c => layerMap[c]);
                layerMap[id] = minConsumerLayer - 1;
            }
        }

        // Normalise so the minimum layer is 0.
        int minLayer = nodes.Min(id => layerMap[id]);
        foreach (int id in nodes)
            layerMap[id] -= minLayer;

        // Group ids into per-layer lists.
        int maxLayer = nodes.Max(id => layerMap[id]);
        var byLayer = Enumerable.Range(0, maxLayer + 1)
            .Select(l => nodes.Where(id => layerMap[id] == l).ToList())
            .ToList();

        // Sort within each layer using the barycenter heuristic to reduce edge crossings.
        SortByBarycenter(byLayer, upstream);

        // Assign pivots left-to-right by layer, top-to-bottom within each layer.
        for (int layer = 0; layer <= maxLayer; layer++)
        {
            float x = OriginX + layer * LayerSpacing;
            float y = OriginY;
            foreach (int id in byLayer[layer])
            {
                result[id] = new PointF(x, y);
                y += NodeSpacing;
            }
        }

        return result;
    }

    // PRIVATE HELPERS ==============================================================

    // Assigns a layer number via recursive longest-path from sources.
    // inProgress guards against cycles (graphs should be acyclic, but just in case).
    private static int AssignLayer(
        int id,
        Dictionary<int, HashSet<int>> upstream,
        Dictionary<int, int> layers,
        HashSet<int> inProgress)
    {
        if (layers.TryGetValue(id, out int existing))
            return existing;

        if (!inProgress.Add(id))
            return 0; // cycle — break it

        int layer = 0;
        if (upstream.TryGetValue(id, out var upSet))
        {
            foreach (int upId in upSet)
                layer = Math.Max(layer, AssignLayer(upId, upstream, layers, inProgress) + 1);
        }

        inProgress.Remove(id);
        layers[id] = layer;
        return layer;
    }

    // Sorts nodes within each layer by their average upstream-neighbour position
    // (barycenter heuristic, three forward passes for better convergence).
    private static void SortByBarycenter(
        List<List<int>> byLayer,
        Dictionary<int, HashSet<int>> upstream)
    {
        var position = new Dictionary<int, float>();

        for (int l = 0; l < byLayer.Count; l++)
            for (int i = 0; i < byLayer[l].Count; i++)
                position[byLayer[l][i]] = i;

        for (int pass = 0; pass < 3; pass++)
        {
            for (int l = 1; l < byLayer.Count; l++)
            {
                foreach (int id in byLayer[l])
                {
                    var upPos = upstream[id]
                        .Where(uid => position.ContainsKey(uid))
                        .Select(uid => position[uid])
                        .ToList();

                    if (upPos.Count > 0)
                        position[id] = upPos.Average();
                }

                byLayer[l] = byLayer[l]
                    .OrderBy(id => position[id])
                    .ToList();

                for (int i = 0; i < byLayer[l].Count; i++)
                    position[byLayer[l][i]] = i;
            }
        }
    }
}
