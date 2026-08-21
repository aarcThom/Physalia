// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Api;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// A model-invoked tool node that searches the RhinoCommon API (Rhino's .NET SDK) by keyword or
/// symbol name. It advertises a <c>search_rhinocommon</c> tool (wire its Tool output into the
/// LLM Call's Tools input); when the model calls it, the dispatched signal arrives from a Router, the
/// node searches the API index, and it emits the matches as a tool result (wire its Result output
/// through a Feedback component into a Feedback Collector and back to the Router's Results input).
///
/// <para>The index is reflected from the installed <c>RhinoCommon.dll</c> and enriched from the
/// paired <c>RhinoCommon.xml</c>, so the signatures returned are exact and version-correct for the
/// user's Rhino. Building it is comparatively expensive, so the node runs asynchronously and builds
/// the index off the solve thread on the first call (cached thereafter) — Grasshopper never freezes.</para>
/// </summary>
public class RhinoCommonSearch : LlmToolComponentBase
{
    private const int DefaultCount = 10;
    private const int MaxCount = 25;

    private static readonly LlmToolDefinition ToolDef = new(
        "search_rhinocommon",
        "Search the RhinoCommon API (Rhino's .NET SDK, usable from Python or C# script components) for types, methods, and properties by keyword or symbol name. Call this before writing code that uses RhinoCommon to get exact, version-correct signatures — return types, parameter types and names, and static/instance — plus summaries. Query with a symbol (\"Brep.CreateFromLoft\"), a type (\"Mesh\"), or keywords (\"offset surface\").",
        "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"A symbol, type name, or keywords describing the API you need.\"},\"count\":{\"type\":\"integer\",\"description\":\"Maximum number of results to return (1-25).\",\"default\":10}},\"required\":[\"query\"]}");

    /// <summary>
    /// Initializes a new instance of the <see cref="RhinoCommonSearch"/> class.
    /// </summary>
    public RhinoCommonSearch()
        : base("RhinoCommon Search", "RC Search", "Lets the model look up the RhinoCommon API — real method signatures and their documentation — before it writes code against them. The cure for invented method names.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7A3C1E92-5D44-4B8F-9C21-3E6A8F0D1B57");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "An API lookup the model has asked for, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises the RhinoCommon lookup to the model: a type or method name in, its real signature and documentation out. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "The signatures and documentation heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    protected override Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        (string query, int count) = ParseArgs(call.InputJson);
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(ToolCallResult.Error("search_rhinocommon requires a non-empty 'query'."));
        }

        // Touches the cached index, building it on first call. This runs on the base's background
        // task, so the one-time reflection + XML parse never blocks the Grasshopper solve thread.
        ApiIndex index = RhinoCommonIndexBuilder.Index;
        if (index.Count == 0)
        {
            return Task.FromResult(ToolCallResult.Error(
                "The RhinoCommon API index could not be built (RhinoCommon could not be reflected)."));
        }

        IReadOnlyList<ApiMember> hits = index.Search(query, count);
        return Task.FromResult(ToolCallResult.Ok(Format(hits, query)));
    }

    private static (string Query, int Count) ParseArgs(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return (string.Empty, DefaultCount);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (inputJson, DefaultCount);
            }

            string query = root.TryGetProperty("query", out JsonElement q) && q.ValueKind == JsonValueKind.String
                ? q.GetString() ?? string.Empty
                : string.Empty;

            int count = DefaultCount;
            if (root.TryGetProperty("count", out JsonElement c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int parsed))
            {
                count = Math.Clamp(parsed, 1, MaxCount);
            }

            return (query, count);
        }
        catch (JsonException)
        {
            return (inputJson, DefaultCount);
        }
    }

    private static string Format(IReadOnlyList<ApiMember> hits, string query)
    {
        if (hits.Count == 0)
        {
            return $"No RhinoCommon members matched \"{query}\". Try a simpler keyword or a type name.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"RhinoCommon members matching \"{query}\":");

        int n = 0;
        foreach (ApiMember member in hits)
        {
            n++;
            string kind = member.IsStatic ? "static " + KindLabel(member.Kind) : KindLabel(member.Kind);
            string header = member.Kind switch
            {
                ApiMemberKind.Type => member.DeclaringType,
                ApiMemberKind.Constructor => member.DeclaringType + " (constructor)",
                _ => member.DeclaringType + "." + member.MemberName,
            };

            builder.AppendLine();
            builder.AppendLine($"{n}. {header}  ({kind})");
            builder.AppendLine("   " + member.Signature);
            if (!string.IsNullOrWhiteSpace(member.Summary))
            {
                builder.AppendLine("   " + Truncate(member.Summary, 300));
            }

            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                builder.AppendLine("   Returns: " + Truncate(member.Returns, 200));
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string KindLabel(ApiMemberKind kind) => kind switch
    {
        ApiMemberKind.Type => "type",
        ApiMemberKind.Constructor => "constructor",
        ApiMemberKind.Method => "method",
        ApiMemberKind.Property => "property",
        ApiMemberKind.Field => "field",
        ApiMemberKind.Event => "event",
        _ => "member",
    };

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value.Substring(0, max) + "…";
    }
}
