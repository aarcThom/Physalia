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
/// Grounds the model with the tools currently in use in the document — the tool nodes wired into a
/// dispatch loop (a Router), collected by the Tools Present grounder. Unlike the other grounding
/// kinds it contributes <b>no</b> system-prompt text: tools are advertised to the model through the
/// provider's native tool-calling API, so folding them into the prompt as prose would double-advertise
/// them. This record instead <i>carries</i> the live tool definitions from the grounder to the Recorder,
/// which lifts them onto the <see cref="ConvoInstruct.Instructions.Tools"/> it mints (and surfaces their
/// names for the chat input's <c>/t/</c> reference).
/// </summary>
/// <param name="Tools">The definitions of every tool in use.</param>
public sealed record ToolsGrounding(IReadOnlyList<ToolDefinition> Tools) : Grounding
{
    /// <inheritdoc/>
    /// <remarks>Empty by design — tools reach the model as native tool definitions, not prompt text.</remarks>
    public override string ToSystemPromptSection() => string.Empty;

    /// <summary>
    /// Gets the names of the carried tools, for the chat input's <c>/t/&lt;toolname&gt;</c> reference.
    /// </summary>
    public IEnumerable<string> ToolNames =>
        Tools is null ? Enumerable.Empty<string>() : Tools.Select(t => t.Name);
}
