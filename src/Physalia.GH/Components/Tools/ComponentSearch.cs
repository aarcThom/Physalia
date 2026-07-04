// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Components;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that searches the user's installed Grasshopper components by keyword.
/// It advertises a <c>search_components</c> tool (wire its Tool output into the Reasoner's Tools
/// input); when the model calls it, the dispatched signal arrives from a Router, the node searches a
/// wired Component Catalog, and it emits the matches as a tool result (wire its Result output through
/// a Feedback component into a Feedback Collector and back to the Router's Results input).
/// </summary>
public class ComponentSearch : ToolComponentBase
{
    private const int InCatalog = 1;

    private const int MaxResults = 15;

    private static readonly ToolDefinition ToolDef = new(
        "search_components",
        "Search the user's installed Grasshopper components by keyword. Call this when you need the exact name of a component to place (for example \"loft\", \"construct point\", or \"voronoi\"). Returns matching component names with their categories and full input/output signatures (Nickname:Type).",
        "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"Keywords describing the component you are looking for.\"}},\"required\":[\"query\"]}");

    private ComponentCatalog? _catalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentSearch"/> class.
    /// </summary>
    public ComponentSearch()
        : base("Component Search", "Search", "A tool the model calls to search the installed component library by keyword.")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C5F2A9D4-6B81-4E37-A0C2-9D4F1B6E8350");

    /// <inheritdoc/>
    protected override ToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ComponentCatalog(), "Component Catalog", "Cat", "Installed-component catalog from a Library component, searched on each call.", GH_ParamAccess.item);
        pManager[InCatalog].Optional = true;
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        var catalogGoo = new GH_ComponentCatalog();
        _catalog = da.GetData(InCatalog, ref catalogGoo) ? catalogGoo?.Value : null;
    }

    /// <inheritdoc/>
    protected override ToolCallResult ExecuteCall(ToolCallContent call)
    {
        if (_catalog is null)
        {
            return ToolCallResult.Error("No component catalog is wired into the search tool — connect a Library component.");
        }

        string query = ExtractQuery(call.InputJson);
        return ToolCallResult.Ok(SearchCatalog(_catalog, query));
    }

    private static string ExtractQuery(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("query", out JsonElement q) &&
                q.ValueKind == JsonValueKind.String)
            {
                return q.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Fall through — treat the raw argument string as the query.
        }

        return inputJson;
    }

    private static string SearchCatalog(ComponentCatalog catalog, string query)
    {
        if (catalog.Count == 0)
        {
            return "The wired component catalog is empty.";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return "No query was provided to search_components.";
        }

        string normalizedQuery = query.ToLowerInvariant().Trim();
        string[] tokens = normalizedQuery.Split(
            new[] { ' ', '\t', ',', '/', '-' },
            StringSplitOptions.RemoveEmptyEntries);

        var hits = catalog.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new { Entry = e, Score = ScoreHit(e.Name, normalizedQuery, tokens) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Name.Length)
            .GroupBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First().Entry)
            .Take(MaxResults)
            .Select(FormatHit)
            .ToList();

        if (hits.Count == 0)
        {
            ComponentMatcher.MatchResult best = ComponentMatcher.Match(query, catalog);
            return best.Entry is not null && best.Score >= 50
                ? $"No direct matches for \"{query}\". Closest: {FormatHit(best.Entry)}."
                : $"No installed components matched \"{query}\".";
        }

        return $"Components matching \"{query}\":\n" + string.Join("\n", hits);
    }

    // Formats one hit as "Name (Category > SubCategory) — in: A:Point, G:Vector; out: C:Curve".
    // Signature introspection is lazy and cached; when it fails the hit degrades to the
    // name-and-category line, never an error.
    private static string FormatHit(CatalogEntry entry)
    {
        string line = $"{entry.Name} ({entry.Category} > {entry.SubCategory})";
        if (!ComponentSignatureProvider.TryGetSignature(entry.ComponentGuid, out var inputs, out var outputs))
        {
            return line;
        }

        string ins = string.Join(", ", inputs.Select(p => SignatureFormat.Port(p.Name, p.TypeHint)));
        string outs = string.Join(", ", outputs.Select(p => SignatureFormat.Port(p.Name, p.TypeHint)));
        return $"{line} — in: {ins}; out: {outs}";
    }

    private static int ScoreHit(string name, string normalizedQuery, IReadOnlyCollection<string> tokens)
    {
        string lowered = name.ToLowerInvariant();
        if (lowered.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return 100;
        }

        int matched = tokens.Count(t => lowered.Contains(t, StringComparison.Ordinal));
        return matched > 0 ? 40 + (matched * 10) : 0;
    }
}
