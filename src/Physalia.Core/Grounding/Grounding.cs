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

    /// <summary>
    /// Gets a value indicating whether this grounding's section is rewritten from turn to turn.
    /// Volatile sections are sorted behind the stable ones so they cannot invalidate a provider's
    /// cached prefix; a grounding that renders the same text all session leaves this false and
    /// rides inside the cache. Override it in any grounding that reads live document state.
    /// </summary>
    public virtual bool IsVolatile => false;
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
            return "These are the ONLY Grasshopper components you may place — use these exact names:\n"
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
        section.Append("These are the ONLY Grasshopper components you may place — use these exact names.");

        if (signatureLines.Count > 0)
        {
            section.Append(" Signatures list parameters as Name:Type in paramIndex order (first = 0); ");
            section.Append("use these exact Names in inputSettings.parameterName. ");
            section.Append("An input marked * is REQUIRED — wire it or internalize a value, ");
            section.Append("or it produces nothing downstream:\n");
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
/// <param name="ModelPlacedCount">
/// How many of the exported components were placed from the model's own previous submissions.
/// Drives the provenance line, so the model never has to infer placement status: zero means
/// nothing of its authorship is on the canvas (a new build is a full document), non-zero means
/// its graph is live (edits are ghpatches).
/// </param>
public sealed record CanvasStateGrounding(string GhJsonText, string Checksum, int ComponentCount, int ModelPlacedCount = 0) : Grounding
{
    /// <summary>
    /// Gets an optional one-shot warning rendered with the section — set when the model's last
    /// placement could not keep its authored component ids, so it is told outright that the ids in
    /// this canvas state are the authoritative renumbering (never its own remembered numbering).
    /// Null renders nothing.
    /// </summary>
    public string? NumberingNote { get; init; }

    /// <summary>
    /// Gets a value indicating whether this state covers only the contents of the master
    /// "Physalia" group — the shared model/user workspace — instead of the whole canvas. The
    /// rendered section explains the visibility contract so the model neither reasons about the
    /// hidden canvas nor wonders where its placed components went.
    /// </summary>
    public bool GroupScoped { get; init; }

    /// <inheritdoc/>
    /// <remarks>
    /// The canvas is re-exported at every mint, so this section differs on essentially every turn.
    /// It is the one grounding that must sit behind the cache breakpoint — with it inside the
    /// cached prefix, nothing in the system prompt would ever cache.
    /// </remarks>
    public override bool IsVolatile => true;

    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (ComponentCount <= 0 || string.IsNullOrWhiteSpace(GhJsonText))
        {
            return string.Empty;
        }

        // The second sentence pre-empts a real failure mode: the checksum deliberately excludes
        // internalized data and slider values (user tweaks must not invalidate patch bases), and
        // without being told, the model reads an unchanged checksum as "my modify silently failed"
        // and burns rounds re-testing it.
        string checksumLine = string.IsNullOrWhiteSpace(Checksum)
            ? string.Empty
            : "\nBase checksum — copy this verbatim into patch.base.checksum: " + Checksum.Trim()
              + "\nIt fingerprints structure only — slider-value and internalized-data edits do not "
              + "change it, so an unchanged checksum never means such a modify failed; trust the "
              + "patch outcome report.";

        // Stated outright so the model never infers placement status from the canvas contents —
        // a rejected submission leaves the canvas unchanged, and inferring "did my graph land?"
        // from first principles is exactly what makes corrective turns wobble between modes.
        string provenanceLine = ModelPlacedCount > 0
            ? "Provenance: " + ModelPlacedCount + " of these components were placed from your previous "
              + "responses — your graph is live; edit it via ghpatch, matched by instanceGuid."
            : "Provenance: NONE of these components came from you — the "
              + (GroupScoped ? "group" : "canvas") + " holds only the user's own work so far. "
              + "To build something new, emit a full GhJSON document (it places alongside the user's "
              + "components); use a ghpatch only to change these existing components.";

        string numberingLine = string.IsNullOrWhiteSpace(NumberingNote)
            ? string.Empty
            : NumberingNote!.Trim() + "\n";

        // The scoped intro states the visibility contract outright: what the model places lands in
        // the group mechanically (no group op needed for that), the hidden canvas is not its
        // business, and the user widens its view by moving components INTO the group.
        string intro = GroupScoped
            ? "This is the CURRENT contents of the 'Physalia' group, serialized as GhJSON — the shared "
              + "workspace between you and the user, and your whole view of the canvas. Everything you "
              + "place is added to this group automatically. The rest of the canvas is hidden: never "
              + "reference or reason about components not shown here. The user can move their own "
              + "components into the group to bring them into your view. "
            : "This is the CURRENT state of the Grasshopper canvas, serialized as GhJSON. It is the "
              + "definition the user is building. ";

        return intro
            + "To EDIT existing components, emit a ghpatch: match them by instanceGuid, reference "
            + "connection endpoints by the integer id shown here (ids are session-stable and never "
            + "reused), change ONLY what the request requires, and never re-emit unchanged "
            + "components. Components marked physalia.rhinoRef reference live Rhino geometry — wire "
            + "FROM them as data sources; never modify, remove, or recreate them.\n"
            + provenanceLine + "\n"
            + numberingLine
            + GhJsonText.Trim()
            + checksumLine;
    }
}

/// <summary>
/// One parameter on a locked script component interface: the variable name, the Physalia
/// type-hint name (empty for untyped inputs, and always empty for outputs — they never carry a
/// hint), and the access mode as its submission-JSON string (<c>item</c>, <c>list</c>, or
/// <c>tree</c>).
/// </summary>
/// <param name="Name">Variable name used inside the script.</param>
/// <param name="TypeHint">Physalia type-hint name (e.g. <c>Number</c>), or empty when untyped.</param>
/// <param name="Access">Access mode string: <c>item</c>, <c>list</c>, or <c>tree</c>.</param>
public sealed record ScriptInterfacePort(string Name, string TypeHint, string Access)
{
    /// <summary>
    /// Gets the type names the canvas downstream of this port already demands — the parameters an
    /// OUTPUT is wired into. Empty when nothing is wired, or when the recipients impose no real
    /// constraint. This is a constraint the script component itself cannot express: an untyped
    /// output is still obliged to produce a Mesh once the user has plugged it into a Mesh
    /// parameter, and code that assigns anything else silently breaks a wire that already exists.
    /// </summary>
    public IReadOnlyList<string> DownstreamTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets what is actually arriving on this port when it is a connected INPUT — read off the live
    /// data, not the declaration. Null when nothing is wired. The two routinely disagree: type hint
    /// and access are the fields users most often leave unset, so a port declared untyped and
    /// item-access may be receiving two curves.
    /// </summary>
    public ScriptPortIncoming? Incoming { get; init; }
}

/// <summary>
/// What is flowing into a connected input at the moment the contract was read.
/// </summary>
/// <param name="Count">How many items arrive.</param>
/// <param name="Branches">How many data-tree branches they arrive in.</param>
/// <param name="TypeName">What they are, or empty when nothing has flowed yet.</param>
public sealed record ScriptPortIncoming(int Count, int Branches, string TypeName);

/// <summary>
/// What a locked interface has to be SAID differently for, one script language to the next: what to
/// call the component, which submission schema the model is answering with, and what "honour this
/// interface" means in that language's code.
///
/// <para>The parameter list itself is language-neutral — a name, a type hint, an access — so only
/// this prose varies. Keeping the variance in one small record is what stops the grounding (and the
/// transmitter's rejection feedback that reuses it) from growing a language branch.</para>
/// </summary>
/// <param name="ComponentKind">What to call the component, e.g. "Python script component".</param>
/// <param name="SchemaName">The submission schema the model emits, e.g. "PythonComponent".</param>
/// <param name="CodeRule">The sentence telling the model how its code must honour the interface.</param>
public sealed record ScriptInterfaceDialect(string ComponentKind, string SchemaName, string CodeRule)
{
    /// <summary>Gets the dialect of the GH Python 3 Script component.</summary>
    public static ScriptInterfaceDialect Python { get; } = new(
        "Python script component",
        "PythonComponent",
        "Write the code against exactly these variable names.");

    /// <summary>
    /// Gets the dialect of the Rhino 8 C# Script component, where the interface is declared a
    /// second time in the code itself and both declarations have to match the lock.
    /// </summary>
    public static ScriptInterfaceDialect CSharp { get; } = new(
        "C# script component",
        "CSharpComponent",
        "Write the RunScript signature to take exactly these parameters and no others — every input "
        + "by value in the order listed, then every output as an `out` argument — and write the code "
        + "against those names.");
}

/// <summary>
/// Grounds the model with the fixed input/output interface of the script component a transmitter
/// drives, and declares that interface locked: existing wires on the canvas depend on these
/// parameters, so every submission the model emits for the component must declare exactly these
/// inputs and outputs — same names, types, and access. Emitted by the Script I/O grounder,
/// whose link also makes the transmitter enforce the contract (it pushes code only, and rejects a
/// submission that declares any other parameter set). Each parameter renders as the exact JSON
/// entry the model should copy into its <c>inputs</c>/<c>outputs</c> arrays.
/// </summary>
/// <param name="ComponentName">The target script component's display (nick) name.</param>
/// <param name="Inputs">The locked input parameters, in interface order.</param>
/// <param name="Outputs">The locked output parameters, in interface order.</param>
/// <param name="Dialect">What the target's language calls things, and what its code must do to honour the lock.</param>
public sealed record ScriptInterfaceGrounding(
    string ComponentName,
    IReadOnlyList<ScriptInterfacePort> Inputs,
    IReadOnlyList<ScriptInterfacePort> Outputs,
    ScriptInterfaceDialect Dialect) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (string.IsNullOrWhiteSpace(ComponentName))
        {
            return string.Empty;
        }

        var section = new System.Text.StringBuilder();
        section.Append("The ").Append(Dialect.ComponentKind).Append(" \"").Append(ComponentName.Trim())
            .Append("\" has a LOCKED interface: existing wires depend on its inputs and outputs, ")
            .Append("so they must be preserved exactly. In every ").Append(Dialect.SchemaName)
            .Append(" JSON you emit for ")
            .Append("this component, declare EXACTLY these parameter NAMES — never add, remove, ")
            .Append("or rename one, because the wires already on the canvas are attached to them ")
            .Append("and a submission naming anything else is rejected without being applied. ")
            .Append("You MAY change a parameter's type hint or access: those are applied in place, ")
            .Append("the wires survive, and where the live data below contradicts the declaration ")
            .Append("you are expected to. ").Append(Dialect.CodeRule).Append('\n');
        section.Append("\"inputs\": ").Append(FormatPorts(Inputs)).Append('\n');
        section.Append("\"outputs\": ").Append(FormatPorts(Outputs));

        string incoming = FormatIncoming(Inputs);
        if (incoming.Length > 0)
        {
            section.Append('\n').Append(incoming);
        }

        string downstream = FormatDownstream(Outputs);
        if (downstream.Length > 0)
        {
            section.Append('\n').Append(downstream);
        }

        return section.ToString();
    }

    /// <summary>
    /// Renders what is actually arriving on the connected inputs, and — where the declaration does
    /// not match the data — says so directly. Two curves landing on an item-access input is the
    /// motivating case: nothing in the declaration reveals it, the script silently sees only the
    /// first curve, and the fix is a declaration change the model is allowed to make.
    /// </summary>
    /// <param name="inputs">The locked input ports.</param>
    /// <returns>The incoming-data section, or empty when nothing is wired.</returns>
    private static string FormatIncoming(IReadOnlyList<ScriptInterfacePort>? inputs)
    {
        var wired = (inputs ?? Array.Empty<ScriptInterfacePort>())
            .Where(p => p.Incoming is not null)
            .ToList();

        if (wired.Count == 0)
        {
            return string.Empty;
        }

        var section = new System.Text.StringBuilder();
        section.Append("LIVE DATA on the connected inputs, as it is right now. Where this ")
            .Append("disagrees with the declaration, the declaration is what is wrong — correct ")
            .Append("it in your submission:\n");

        foreach (ScriptInterfacePort port in wired)
        {
            ScriptPortIncoming data = port.Incoming!;
            string what = data.TypeName.Length > 0 ? data.TypeName : "unknown type";

            section.Append("  ").Append(port.Name.Trim()).Append(" ← ")
                .Append(data.Count).Append(' ').Append(what)
                .Append(data.Count == 1 ? string.Empty : "s");

            if (data.Branches > 1)
            {
                section.Append(" across ").Append(data.Branches).Append(" branches");
            }

            string advice = AccessAdvice(port.Access, data);
            if (advice.Length > 0)
            {
                section.Append("  — ").Append(advice);
            }

            section.Append('\n');
        }

        return section.ToString().TrimEnd();
    }

    /// <summary>
    /// The mismatch between a port's declared access and what is actually arriving, phrased as the
    /// correction to make. Empty when the declaration already fits.
    /// </summary>
    /// <param name="access">The declared access (<c>item</c>, <c>list</c>, or <c>tree</c>).</param>
    /// <param name="data">What is arriving.</param>
    /// <returns>The advice, or empty.</returns>
    private static string AccessAdvice(string access, ScriptPortIncoming data)
    {
        string declared = (access ?? string.Empty).Trim().ToLowerInvariant();

        if (data.Branches > 1 && declared != "tree")
        {
            return $"declared \"{declared}\" but the data arrives in {data.Branches} branches; "
                + "use \"tree\" to see all of it, or accept that only one branch is processed per solve";
        }

        if (data.Count > 1 && declared == "item")
        {
            return $"declared \"item\" but {data.Count} items arrive, so the script would see only "
                + "the first — change this input's access to \"list\" and write the code to iterate";
        }

        return string.Empty;
    }

    /// <summary>
    /// Renders what the canvas downstream of the outputs already demands, as prose rather than as
    /// JSON entries: the submission schemas forbid a <c>type</c> on an output
    /// (<c>additionalProperties: false</c>), so a wired-up expectation cannot ride inside the
    /// copyable array without producing a submission that fails validation. It is a constraint on
    /// the VALUE the code assigns, not on the declaration, and is stated as such.
    /// </summary>
    /// <param name="outputs">The locked output ports.</param>
    /// <returns>The downstream section, or empty when nothing is wired.</returns>
    private static string FormatDownstream(IReadOnlyList<ScriptInterfacePort>? outputs)
    {
        var wired = (outputs ?? Array.Empty<ScriptInterfacePort>())
            .Where(p => p.DownstreamTypes.Count > 0)
            .ToList();

        if (wired.Count == 0)
        {
            return string.Empty;
        }

        var section = new System.Text.StringBuilder();
        section.Append("These outputs are ALREADY WIRED into typed inputs on the canvas. Do not ")
            .Append("declare a type for them — the schema forbids it — but the value your code ")
            .Append("assigns must be readable as the type listed, because a value Grasshopper ")
            .Append("cannot convert breaks a connection the user has already made:\n");

        foreach (ScriptInterfacePort port in wired)
        {
            section.Append("  ").Append(port.Name.Trim()).Append(" → ")
                .Append(string.Join(", ", port.DownstreamTypes))
                .Append(port.DownstreamTypes.Count > 1 ? "  (wired to several — assign something every one of them accepts)" : string.Empty)
                .Append('\n');
        }

        return section.ToString().TrimEnd();
    }

    /// <summary>
    /// Renders the locked parameters of one side as the exact JSON array the model should emit,
    /// omitting the <c>type</c> field for untyped ports (and for outputs, which never carry one).
    /// </summary>
    /// <param name="ports">The locked ports, or an empty list.</param>
    /// <returns>A JSON array literal, one entry per line.</returns>
    public static string FormatPorts(IReadOnlyList<ScriptInterfacePort>? ports)
    {
        if (ports is null || ports.Count == 0)
        {
            return "[]";
        }

        var entries = ports.Select(p =>
        {
            string type = string.IsNullOrWhiteSpace(p.TypeHint)
                ? string.Empty
                : $" \"type\": \"{p.TypeHint.Trim()}\",";
            return $"  {{ \"name\": \"{p.Name.Trim()}\",{type} \"access\": \"{p.Access.Trim()}\" }}";
        });

        return "[\n" + string.Join(",\n", entries) + "\n]";
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
public sealed record ToolsGrounding(IReadOnlyList<LlmToolDefinition> Tools) : Grounding
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
