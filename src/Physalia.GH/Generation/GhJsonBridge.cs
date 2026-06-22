// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using GhJSON.Core;
using GhJSON.Core.SchemaModels;
using GhJSON.Core.Serialization;
using GhJSON.Grasshopper;
using GhJSON.Grasshopper.PutOperations;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Physalia.Core.Catalog;
using Physalia.GH.Components;

namespace Physalia.GH.Generation;

/// <summary>
/// Result returned by <see cref="GhJsonBridge.LoadAndPlace"/>.
/// </summary>
internal sealed record PlaceResult(
    bool Success,
    int ComponentCount,
    int ConnectionCount,
    int WarningCount,
    string? ErrorMessage,
    IReadOnlyList<Guid> PlacedGuids,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> UnfixedIssues);

/// <summary>
/// Façade over the GhJSON library. All direct GhJSON API calls originate here;
/// components interact with the library exclusively through this class.
/// </summary>
internal static class GhJsonBridge
{
    /// <summary>
    /// True while <see cref="LoadAndPlace"/> is executing. Components that auto-place
    /// Pickers in <c>AddedToDocument</c> check this flag to suppress duplicate placement
    /// when the component was already wired in the imported file.
    /// </summary>
    internal static bool IsImporting { get; private set; }
    /// <summary>
    /// Exports the Grasshopper objects identified by <paramref name="guids"/> to a
    /// <c>.ghjson</c> file at <paramref name="path"/>.
    /// </summary>
    /// <param name="guids">The instance GUIDs of the objects to export.</param>
    /// <param name="path">The destination file path.</param>
    internal static void ExportToFile(IReadOnlyList<Guid> guids, string path)
    {
        GhJsonDocument doc = GhJsonGrasshopper.GetByGuids(guids);
        StripNickNames(doc);
        GhJson.ToFile(doc, path, new WriteOptions { Indented = true });
    }

    /// <summary>
    /// Serialises a <see cref="PhySchemaDocument"/> (the LLM-authored, position-free definition)
    /// to a GhJSON string, stamping each component with the canvas pivot computed by the caller.
    /// </summary>
    /// <param name="schema">The parsed PhySchema document.</param>
    /// <param name="pivots">Canvas pivot positions keyed by component id; components without an
    /// entry fall back to (50, 50).</param>
    /// <returns>The GhJSON document as an indented string.</returns>
    internal static string SerializePhySchema(PhySchemaDocument schema, IReadOnlyDictionary<int, PointF> pivots)
    {
        var components = new List<GhJsonComponent>();
        foreach (PhySchemaComponent component in schema.Components ?? Array.Empty<PhySchemaComponent>())
        {
            PointF pivot = pivots.TryGetValue(component.Id, out PointF p) ? p : new PointF(50f, 50f);

            var ghComponent = new GhJsonComponent
            {
                Name = component.Name,
                Id = component.Id,
                Pivot = new GhJsonPivot(pivot.X, pivot.Y),
            };

            if (Guid.TryParse(component.InstanceGuid, out Guid instanceGuid))
            {
                ghComponent.InstanceGuid = instanceGuid;
            }

            if (component.ComponentState is JsonElement state)
            {
                ghComponent.ComponentState = JsonConvert.DeserializeObject<GhJsonComponentState>(state.GetRawText());
            }

            components.Add(ghComponent);
        }

        var connections = new List<GhJsonConnection>();
        foreach (PhySchemaConnection connection in schema.Connections ?? Array.Empty<PhySchemaConnection>())
        {
            connections.Add(new GhJsonConnection
            {
                From = new GhJsonConnectionEndpoint { Id = connection.From.Id, ParamName = connection.From.ParamName },
                To = new GhJsonConnectionEndpoint { Id = connection.To.Id, ParamName = connection.To.ParamName },
            });
        }

        var doc = new GhJsonDocument("1.0", metadata: null, components, connections, groups: null);
        return GhJson.ToJson(doc, new WriteOptions { Indented = true });
    }

    /// <summary>
    /// Returns whether <paramref name="json"/> is a valid GhJSON document.
    /// </summary>
    /// <param name="json">The JSON string to validate.</param>
    /// <param name="message">Validation failure message, or null on success.</param>
    /// <returns>true if valid; false otherwise.</returns>
    internal static bool IsValidJson(string json, out string? message)
    {
        return GhJson.IsValid(json, out message);
    }

    /// <summary>
    /// Rewrites each component's name in a GhJSON string to a real installed component, matched
    /// against <paramref name="catalog"/>, and stamps the resolved component-type GUID so the
    /// library can create it exactly. Components that already carry a non-empty
    /// <c>componentGuid</c> are left untouched. Names that cannot be matched confidently are
    /// returned for feedback rather than guessed.
    /// </summary>
    /// <param name="json">The GhJSON document as a string.</param>
    /// <param name="catalog">The installed-component catalog to resolve against.</param>
    /// <returns>The resolved GhJSON and the list of names that could not be resolved.</returns>
    internal static (string Json, IReadOnlyList<string> Unresolved) ResolveComponentNames(string json, ComponentCatalog catalog)
    {
        GhJsonDocument doc = GhJson.FromJson(json);
        var unresolved = new List<string>();

        if (doc.Components is not null)
        {
            foreach (GhJsonComponent component in doc.Components)
            {
                if (component.ComponentGuid is not null && component.ComponentGuid.Value != Guid.Empty)
                {
                    continue;
                }

                string proposed = component.Name ?? string.Empty;
                ComponentMatcher.MatchResult match = ComponentMatcher.Match(proposed, catalog);

                if (match.IsConfident && match.Entry is not null)
                {
                    component.Name = match.Entry.Name;
                    component.ComponentGuid = match.Entry.ComponentGuid;
                }
                else
                {
                    unresolved.Add(string.IsNullOrWhiteSpace(proposed) ? "(unnamed component)" : proposed);
                }
            }
        }

        return (GhJson.ToJson(doc), unresolved);
    }

    /// <summary>
    /// Loads a <c>.ghjson</c> file and places its components onto the active Grasshopper
    /// canvas, with the content's top-left pivot aligned to <paramref name="targetOrigin"/>.
    /// </summary>
    /// <param name="path">Path to the <c>.ghjson</c> file.</param>
    /// <param name="targetOrigin">Canvas position for the top-left corner of the placed content.</param>
    /// <returns>A <see cref="PlaceResult"/> describing the outcome.</returns>
    internal static PlaceResult LoadAndPlace(string path, PointF targetOrigin)
    {
        return PlaceDocument(GhJson.FromFile(path), targetOrigin);
    }

    /// <summary>
    /// Parses a GhJSON string and places its components onto the active Grasshopper canvas,
    /// with the content's top-left pivot aligned to <paramref name="targetOrigin"/>.
    /// </summary>
    /// <param name="json">The GhJSON document as a string.</param>
    /// <param name="targetOrigin">Canvas position for the top-left corner of the placed content.</param>
    /// <returns>A <see cref="PlaceResult"/> describing the outcome.</returns>
    internal static PlaceResult LoadAndPlaceJson(string json, PointF targetOrigin)
    {
        return PlaceDocument(GhJson.FromJson(json), targetOrigin);
    }

    /// <summary>
    /// Places the components of an already-parsed GhJSON document onto the active Grasshopper
    /// canvas, with the content's top-left pivot aligned to <paramref name="targetOrigin"/>.
    /// </summary>
    /// <param name="doc">The parsed GhJSON document.</param>
    /// <param name="targetOrigin">Canvas position for the top-left corner of the placed content.</param>
    /// <returns>A <see cref="PlaceResult"/> describing the outcome.</returns>
    private static PlaceResult PlaceDocument(GhJsonDocument doc, PointF targetOrigin)
    {
        if (doc.Components is null || doc.Components.Count == 0)
        {
            return new PlaceResult(false, 0, 0, 0, "The GhJSON file contains no components to place.", Array.Empty<Guid>(), Array.Empty<string>(), Array.Empty<string>());
        }

        // Normalise AI-authored JSON (missing ids, stray fields, near-miss structure) before placing.
        // The GhJSON library's fixer is the single source of repair; anything it cannot fix is surfaced
        // to the caller so it can be routed back to the model as feedback.
        var fixResult = GhJson.Fix(doc);
        doc = fixResult.Document;
        IReadOnlyList<string> unfixedIssues = fixResult.UnfixedIssues ?? (IReadOnlyList<string>)Array.Empty<string>();

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.Pivot is not null)
            {
                minX = Math.Min(minX, (float)component.Pivot.X);
                minY = Math.Min(minY, (float)component.Pivot.Y);
            }
        }

        if (minX == float.MaxValue)
        {
            minX = 0f;
            minY = 0f;
        }

        var options = new PutOptions
        {
            Offset = new PointF(targetOrigin.X - minX, targetOrigin.Y - minY),
            AutoOffset = false,
            CreateConnections = true,
            CreateGroups = true,
            RegenerateInstanceGuids = true,
            SkipInvalidComponents = true,
            SelectPlacedObjects = true,
        };

        IsImporting = true;
        PutResult result;
        try
        {
            result = GhJsonGrasshopper.Put(doc, options);
        }
        finally
        {
            IsImporting = false;
        }

        if (result.Success)
        {
            ComponentHelpers.ApplyNickNameDisplay(result.PlacedObjects);
        }

        return result.Success
            ? new PlaceResult(true, result.ComponentsPlaced, result.ConnectionsCreated, result.Warnings.Count, null, result.PlacedObjects.Select(o => o.InstanceGuid).ToList(), result.Warnings.ToList(), unfixedIssues)
            : new PlaceResult(false, 0, 0, 0, result.ErrorMessage, Array.Empty<Guid>(), Array.Empty<string>(), unfixedIssues);
    }

    /// <summary>
    /// Removes abbreviated <c>nickName</c> entries from all parameter settings in a document so
    /// that the exported JSON contains only the full <c>parameterName</c> values.
    /// </summary>
    /// <param name="doc">The document to normalise in place.</param>
    private static void StripNickNames(GhJsonDocument doc)
    {
        if (doc.Components is null)
        {
            return;
        }

        foreach (GhJsonComponent component in doc.Components)
        {
            if (component.InputSettings is not null)
            {
                foreach (GhJsonParameterSettings s in component.InputSettings)
                {
                    s.NickName = null;
                }
            }

            if (component.OutputSettings is not null)
            {
                foreach (GhJsonParameterSettings s in component.OutputSettings)
                {
                    s.NickName = null;
                }
            }
        }
    }

}
