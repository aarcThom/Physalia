// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.Common;
using Physalia.Core.Grounding.Clusters;
using Physalia.Core.Grounding.Components;

namespace Physalia.Core.Grounding;

/// <summary>
/// Model-grounding context folded into the system prompt. A discriminated union (closed set of
/// records) so that every kind of grounding — an installed-component catalog, a Grasshopper
/// cluster, a python function — grounds the model the same way: by contributing a labelled
/// section of text. New grounding kinds are added by writing one record that overrides
/// <see cref="ToSystemPromptSection"/>; there is no central switch to edit.
/// </summary>
public abstract record Grounding
{
    /// <summary>
    /// Renders this grounding as a self-contained section to append to the system prompt.
    /// </summary>
    /// <returns>
    /// The section text, or an empty/whitespace string to contribute nothing (the assembler
    /// drops empty sections).
    /// </returns>
    public abstract string ToSystemPromptSection();
}

/// <summary>
/// Grounds the model with the names of the components installed in the user's Grasshopper, so it
/// favours names that actually exist. Wraps the live <see cref="ComponentCatalog"/> snapshot.
/// </summary>
/// <param name="Catalog">The installed-component catalog.</param>
public sealed record ComponentCatalogGrounding(ComponentCatalog Catalog) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (Catalog is null || Catalog.Count == 0)
        {
            return string.Empty;
        }

        return "These Grasshopper components are installed and available. Use these exact names where one fits:\n"
            + string.Join(", ", Catalog.ComponentNames);
    }
}

/// <summary>
/// Grounds the model with the Grasshopper clusters available in the user's <c>Files/CLUSTERS</c>
/// folder. Each cluster is rendered as a component-like signature (name, inputs, outputs, optional
/// description) so the model can reference and wire a cluster exactly as it would a component — the
/// placement layer recognises the referenced name as a cluster and instantiates it from its file.
/// </summary>
/// <param name="Catalog">The available-cluster catalog.</param>
public sealed record ClusterCatalogGrounding(ClusterCatalog Catalog) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (Catalog is null || Catalog.Count == 0)
        {
            return string.Empty;
        }

        var lines = Catalog.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(FormatEntry)
            .ToList();

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        return "These Grasshopper clusters are available — reference one by its exact name (like a "
            + "component) where it fits:\n" + string.Join("\n", lines);
    }

    // Renders one cluster as "- Name(in: A:Number, B:Point) -> (out: R:Brep): description".
    private static string FormatEntry(ClusterEntry entry)
    {
        string inputs = string.Join(", ", entry.Inputs.Select(FormatPort));
        string outputs = string.Join(", ", entry.Outputs.Select(FormatPort));
        string signature = $"- {entry.Name.Trim()}(in: {inputs}) -> (out: {outputs})";
        return string.IsNullOrWhiteSpace(entry.Description)
            ? signature
            : signature + ": " + entry.Description.Trim();
    }

    private static string FormatPort(ClusterPort port) =>
        string.IsNullOrWhiteSpace(port.TypeHint) ? port.Name : $"{port.Name}:{port.TypeHint}";
}

/// <summary>
/// Grounds the model with the unit system of the active Rhino/Grasshopper document, so numeric
/// values and geometry it produces match the document's units. The <see cref="Units"/> string is the
/// text handed to the model — either the live document units or a user-chosen override; the override
/// never changes the document itself.
/// </summary>
/// <param name="Units">The unit-system display name (e.g. <c>Millimeters</c>, <c>Inches</c>).</param>
public sealed record DocumentUnitsGrounding(string Units) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (string.IsNullOrWhiteSpace(Units))
        {
            return string.Empty;
        }

        return $"The active Rhino/Grasshopper document uses these units: {Units.Trim()}. "
            + "Produce geometry and numeric values consistent with this unit system.";
    }
}

/// <summary>
/// Grounds the model with a python function available for use.
/// </summary>
/// <param name="Signature">The function signature (e.g. <c>def foo(a, b) -> float</c>).</param>
/// <param name="Docstring">A description of what the function does.</param>
/// <remarks>
/// Scaffold: today this carries a free-text signature and docstring. TODO: parse the real
/// signature and docstring from the function source.
/// </remarks>
public sealed record PythonFunctionGrounding(string Signature, string Docstring) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (string.IsNullOrWhiteSpace(Signature))
        {
            return string.Empty;
        }

        string body = string.IsNullOrWhiteSpace(Docstring) ? string.Empty : "\n" + Docstring.Trim();
        return $"The following python function is available — use it where it fits:\n{Signature.Trim()}" + body;
    }
}

/// <summary>
/// Grounds the model with the inputs already placed on the canvas — the Rhino-referenced parameters
/// dropped by the Rhino Geometry tool (or otherwise present). The model can wire one into a graph it
/// generates instead of recreating it: it references the input by its exact name (as a node's nickName)
/// and the placement layer splices the graph onto the live parameter. Without this, the model has no
/// way to know those inputs exist and duplicates them.
/// </summary>
/// <param name="Inputs">The referenceable canvas inputs (name + geometry type).</param>
public sealed record CanvasInputGrounding(IReadOnlyList<CanvasInput> Inputs) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (Inputs is null || Inputs.Count == 0)
        {
            return string.Empty;
        }

        var lines = Inputs
            .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Name))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => string.IsNullOrWhiteSpace(i.TypeName) ? $"- {i.Name.Trim()}" : $"- {i.Name.Trim()} ({i.TypeName.Trim()})")
            .ToList();

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        return "These inputs already exist on the canvas — to use one, reference it by its exact name "
            + "(set it as a node's nickName) instead of recreating it, and the graph will be wired onto "
            + "the existing object:\n" + string.Join("\n", lines);
    }
}

/// <summary>
/// One referenceable input already on the canvas: its unique reference name and the geometry type it
/// carries (e.g. <c>Curve</c>, <c>Point</c>).
/// </summary>
/// <param name="Name">The unique name the model references (the parameter's nickname).</param>
/// <param name="TypeName">The geometry type the input carries.</param>
public sealed record CanvasInput(string Name, string TypeName);

/// <summary>
/// Grounds the model with the fact that it has a persistent memory available through the
/// <c>memory</c> tool, and instructs it to use that memory the way a Claude memory tool would — but as
/// an explicit prompt section so the nudge reaches OpenAI and Gemini too, which (unlike Claude) never
/// have memory instructions auto-injected. When this grounding is not wired, the model is told nothing
/// about memory, so the feature is entirely opt-in: no memory grounding, no memory behaviour.
///
/// <para>The memory is split into two scopes the model addresses by path: <c>/memories/global</c> is
/// shared across every Grasshopper document, and <c>/memories/local</c> is specific to the document
/// being worked on. The user can steer a write with the chat input's <c>/m/global</c> and
/// <c>/m/local</c> references, but the model should also decide for itself where a fact belongs.</para>
/// </summary>
public sealed record MemoryGrounding : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection() =>
        "You have a persistent memory that survives across conversations, provided through the "
        + "\"memory\" tool. Treat it as your notebook for this project.\n"
        + "- At the START of a task, view your memory (call memory with command \"view\" and path "
        + "\"/memories\") and read any file that looks relevant before doing other work.\n"
        + "- Record durable facts as you learn them: user preferences, project conventions, decisions, "
        + "and anything you would want to know next time. Do NOT record transient chatter.\n"
        + "- Memory has two scopes. Files under \"/memories/global\" are shared across every "
        + "Grasshopper document; files under \"/memories/local\" belong only to the current document. "
        + "Put document-specific facts in local memory and broadly useful facts in global memory.\n"
        + "- Store memories as Markdown (.md) files with clear names, and keep them organised — edit or "
        + "delete stale entries instead of piling on duplicates.";
}

/// <summary>
/// Grounds the model with the tools currently in use in the document — the tool nodes wired into a
/// dispatch loop (a Router), collected by the Tools Present grounder. It carries the live tool
/// definitions from the grounder to the Recorder, which lifts them onto the
/// <see cref="ConvoInstruct.Instructions.Tools"/> it mints (and surfaces their names for the chat
/// input's <c>/t/</c> reference). It also folds an explicit list of those tool names into the system
/// prompt: the provider's native tool-calling API already advertises them, but stating the closed set
/// in the prompt — "call only these" — stops the model from attempting a tool that is not actually on
/// the canvas (one it recalls from earlier in the conversation, or a built-in it might otherwise reach
/// for). The Recorder renders this from the <i>selected</i> (advertised) tools, so the list always
/// matches what the model can actually call.
/// </summary>
/// <param name="Tools">The definitions of every tool in use.</param>
public sealed record ToolsGrounding(IReadOnlyList<ToolDefinition> Tools) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        var names = ToolNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return string.Empty;
        }

        return "The only tools available to you are the ones listed here. Call ONLY these tools — never "
            + "invent a tool, and never attempt to call a tool that is not in this list:\n"
            + string.Join(", ", names);
    }

    /// <summary>
    /// Gets the names of the carried tools, for the chat input's <c>/t/&lt;toolname&gt;</c> reference.
    /// </summary>
    public IEnumerable<string> ToolNames =>
        Tools is null ? Enumerable.Empty<string>() : Tools.Select(t => t.Name);
}
