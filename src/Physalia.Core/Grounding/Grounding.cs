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
/// Grounds the model with the names of the components installed in the user's Grasshopper —
/// native and plug-in libraries alike — and declares that list the authoritative set of what may
/// be placed, so the preambles/schemas can defer "what is allowed" to this section instead of
/// hard-coding a native-only policy. Wraps the live <see cref="ComponentCatalog"/> snapshot
/// (already narrowed to the user's grounding selection when one is set).
/// </summary>
/// <param name="Catalog">The installed-component catalog.</param>
/// <param name="IncludeSignatures">
/// True to render each component with its typed input/output signature (one line per component)
/// instead of the flat name list. Signature lines require enriched entries — entries whose ports
/// were never read fall back to a name-only line.
/// </param>
public sealed record ComponentCatalogGrounding(ComponentCatalog Catalog, bool IncludeSignatures = false) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (Catalog is null || Catalog.Count == 0)
        {
            return string.Empty;
        }

        if (!IncludeSignatures)
        {
            return "These Grasshopper components are installed and available — native and plug-in alike. "
                + "This list is the authoritative catalogue of what may be placed: use these exact names, "
                + "and only components from this list:\n"
                + string.Join(", ", Catalog.ComponentNames);
        }

        var entries = Catalog.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Enriched entries get one signature line each; the (usually long) unenriched tail is
        // collapsed into a single comma-joined paragraph — per-line bullets for bare names would
        // waste thousands of tokens saying nothing a flat list doesn't.
        var signatureLines = entries.Where(HasSignature).Select(FormatEntry).ToList();
        var nameOnly = entries.Where(e => !HasSignature(e)).Select(e => e.Name.Trim()).ToList();

        if (signatureLines.Count == 0 && nameOnly.Count == 0)
        {
            return string.Empty;
        }

        var section = new System.Text.StringBuilder();
        section.Append("These Grasshopper components are installed and available — native and plug-in alike. ");
        section.Append("This list is the authoritative catalogue of what may be placed: use these exact names, ");
        section.Append("and only components from this list.");

        if (signatureLines.Count > 0)
        {
            section.Append(" Each signature entry shows its input and output parameters as Name:Type, ");
            section.Append("listed in paramIndex order — the first parameter is paramIndex 0; ");
            section.Append("use these exact Names in inputSettings.parameterName. ");
            section.Append("An input marked * is REQUIRED: it has no built-in default, so wire it or ");
            section.Append("internalize a value — left empty it produces nulls or nothing downstream. ");
            section.Append("Supply data matching these types:\n");
            section.Append(string.Join("\n", signatureLines));
        }

        if (nameOnly.Count > 0)
        {
            section.Append(signatureLines.Count > 0
                ? "\nAlso installed (names only): "
                : "\n");
            section.Append(string.Join(", ", nameOnly));
        }

        return section.ToString();
    }

    // True when the entry's ports were introspected, so it can render a full signature line.
    private static bool HasSignature(CatalogEntry entry) => entry.Inputs is not null && entry.Outputs is not null;

    // Renders one enriched component as "- Name(in: A:Point*, G:Vector) -> (out: C:Curve)",
    // where * marks a required input (no built-in default).
    private static string FormatEntry(CatalogEntry entry)
    {
        string inputs = string.Join(", ", entry.Inputs!.Select(p => SignatureFormat.Port(p.Name, p.TypeHint, p.Required)));
        string outputs = string.Join(", ", entry.Outputs!.Select(p => SignatureFormat.Port(p.Name, p.TypeHint)));
        return $"- {entry.Name.Trim()}(in: {inputs}) -> (out: {outputs})";
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
        SignatureFormat.Port(port.Name, port.TypeHint);
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
/// One parameter on the canvas that references live geometry in the Rhino model: its unique name
/// (the parameter's nickname) and the geometry type it carries (e.g. <c>Curve</c>, <c>Point</c>).
/// A plain DTO for the chat window's Referenced Rhino Geometry page — the model itself learns of
/// these parameters through the canvas state, where they carry the <c>physalia.rhinoRef</c> marker.
/// </summary>
/// <param name="Name">The unique name (the parameter's nickname).</param>
/// <param name="TypeName">The geometry type the parameter carries.</param>
public sealed record ReferencedGeometryInput(string Name, string TypeName);

/// <summary>
/// Grounds the model with the current state of the Grasshopper canvas — the user's work product
/// serialized as GhJSON — so it can edit the definition incrementally instead of regenerating it.
/// When this section is present the model is instructed to emit a ghpatch (add/modify/remove
/// operations matched by instanceGuid) that changes only what the request requires; when the canvas
/// is empty the section contributes nothing and the model naturally falls back to emitting a full
/// GhJSON document. The checksum is a fingerprint of the exact exported text: the model copies it
/// verbatim into <c>patch.base.checksum</c>, and the placement layer refuses to apply a patch whose
/// checksum no longer matches a fresh export (the canvas changed since the model last saw it).
/// </summary>
/// <param name="GhJsonText">The canvas state serialized as GhJSON.</param>
/// <param name="Checksum">Fingerprint of <paramref name="GhJsonText"/> (e.g. <c>sha256-…</c>).</param>
/// <param name="ComponentCount">Number of components in the export; zero renders nothing.</param>
public sealed record CanvasStateGrounding(string GhJsonText, string Checksum, int ComponentCount) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (ComponentCount <= 0 || string.IsNullOrWhiteSpace(GhJsonText))
        {
            return string.Empty;
        }

        string checksumLine = string.IsNullOrWhiteSpace(Checksum)
            ? string.Empty
            : "\nBase checksum — copy this verbatim into patch.base.checksum: " + Checksum.Trim();

        return "This is the CURRENT state of the Grasshopper canvas, serialized as GhJSON. It is the "
            + "definition the user is building — edit it incrementally by emitting a ghpatch document: "
            + "match existing components by their instanceGuid, reference connection endpoints by the "
            + "integer id shown here (ids are stable for the whole session: a component keeps its id "
            + "across turns, components you add keep the ids you gave them, and a removed component's "
            + "id is never reused), and change ONLY what the request requires. Never re-emit "
            + "components that already exist and are not being changed. Components marked with the "
            + "physalia.rhinoRef extension reference live geometry in the Rhino model — wire FROM them "
            + "as data sources; never modify their values, remove them, or recreate them.\n"
            + GhJsonText.Trim()
            + checksumLine;
    }
}

/// <summary>
/// Grounds the model with the tools currently in use in the document — the tool nodes wired into a
/// dispatch loop (a Router), collected by the Tools Present grounder. It carries the live tool
/// definitions from the grounder to the Conversation Log, which lifts them onto the
/// <see cref="ConvoInstruct.Instructions.Tools"/> it mints (and surfaces their names for the chat
/// input's <c>/t/</c> reference). It also folds an explicit list of those tool names into the system
/// prompt: the provider's native tool-calling API already advertises them, but stating the closed set
/// in the prompt — "call only these" — stops the model from attempting a tool that is not actually on
/// the canvas (one it recalls from earlier in the conversation, or a built-in it might otherwise reach
/// for). The Conversation Log renders this from the <i>selected</i> (advertised) tools, so the list always
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
