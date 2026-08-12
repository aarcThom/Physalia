// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// Takes LLM-generated C# (validated against CSharpComponent.json, arriving as the consumed
/// signal's payload) and pushes its code, inputs, and outputs into a linked Rhino 8 C# Script
/// component, then reads back the target's compile and runtime errors. On a clean run it routes the
/// linked component's GUID forward on the Success Signal (so a downstream Runtime Health Check or
/// Geometry Observation can scope to it); on errors it routes the messages back on the Fail Signal.
/// Link to the target by dragging the harness proxy's "C#" grip onto it, or from this node's
/// right-click picker.
///
/// <para>A C# script component is not a Python one with a different keyword set: its parameters are
/// declared twice over — once in the submission, once in the <c>RunScript</c> signature the engine
/// reads out of the source — and the two must agree or the component binds nothing. So the push is
/// gated on a signature check (see <see cref="TryCheckSignature"/>) that rejects a disagreeing
/// submission before anything reaches the canvas, with the expected signature spelled out in the
/// feedback. None of PyTransmitter's marshalling repairs apply here: they exist for the Python
/// engine's value wrapping, and the C# engine hands GH typed values already.</para>
///
/// <para>A <see cref="ScriptIO"/> can be grip-linked to this transmitter exactly as it can to
/// the Python one, and the two checks then compose: the lock pins the declared parameters to the
/// ones already on the component, the signature check pins the code to the declared parameters, so
/// the pushed code binds to the wires the target already has. Unlike Python, a locked submission
/// must declare the interface WHOLE — see <see cref="AllowsPartialInterface"/>.</para>
/// </summary>
public class CsTransmitter : ScriptTransmitterBase
{
    /// <summary>
    /// The class declaration the RhinoCode engine looks for before it will treat the source as a
    /// script instance — it rewrites this base type to its own at compile time. Source without it
    /// is a different (top-level statement) template that declares no parameters at all.
    ///
    /// <para>Copied verbatim from the engine's own pattern, whitespace classes and all: a guard
    /// looser than the thing it guards passes source that then fails on the canvas, which is the
    /// one outcome this check exists to prevent. Note the engine wants a space on BOTH sides of the
    /// colon.</para>
    /// </summary>
    private static readonly Regex ScriptInstanceClass = new(
        @"public\s+class\s+Script_Instance\s+:\s+GH_ScriptInstance",
        RegexOptions.Compiled);

    /// <summary>
    /// The entry point the engine reads the parameter set out of, again its own pattern: by-value
    /// parameters are inputs, <c>out</c>/<c>ref</c> parameters are outputs.
    /// </summary>
    private static readonly Regex RunScriptSignature = new(
        @"private\s+(async\s+)?void\s+RunScript\((?<params>[^)]*)\)",
        RegexOptions.Compiled);

    /// <summary>
    /// The C# type each Physalia type hint must be spelled as in the RunScript signature, so the
    /// converter the pushed parameter carries and the argument the engine binds to it agree. Mirrors
    /// <c>GhPythonBridge</c>'s hint-to-CLR map, in source form — this is what the rejection feedback
    /// quotes back, and what the CSharpComponent schema documents.
    /// </summary>
    private static readonly Dictionary<string, string> CSharpTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Number"] = "double",
        ["Integer"] = "int",
        ["Boolean"] = "bool",
        ["Text"] = "string",
        ["Point"] = "Point3d",
        ["Vector"] = "Vector3d",
        ["Plane"] = "Plane",
        ["Line"] = "Line",
        ["Circle"] = "Circle",
        ["Arc"] = "Arc",
        ["Curve"] = "Curve",
        ["Surface"] = "Surface",
        ["Brep"] = "Brep",
        ["Mesh"] = "Mesh",
        ["Geometry"] = "GeometryBase",
        ["Box"] = "Box",
        ["Transform"] = "Transform",
        ["Interval"] = "Interval",
        ["Colour"] = "System.Drawing.Color",
    };

    private string? _pushError;
    private string? _rejection;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsTransmitter"/> class.
    /// </summary>
    public CsTransmitter()
        : base(
            "C# Transmitter",
            "CsTx",
            "Pushes LLM-generated C# into a linked Rhino 8 C# Script component and routes its errors. Drag the harness's \"C#\" grip to the target C# component.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3F6A9D14-8B27-4C50-A1E9-7D4C2B6F8035");

    /// <inheritdoc/>
    public override string OutletLabel => "C#";

    /// <inheritdoc/>
    public override WireGradient OutletGradient => ArrowStyles.CsTransmitter;

    /// <inheritdoc/>
    public override ScriptInterfaceDialect Dialect => ScriptInterfaceDialect.CSharp;

    /// <inheritdoc/>
    protected override string TargetKind => "C# Script";

    /// <inheritdoc/>
    /// <remarks>
    /// A locked C# submission must declare the WHOLE interface, not a subset. The RunScript
    /// signature is the component's second declaration of its parameters, and a parameter the
    /// signature omits has nothing on the target to bind to — where a Python script simply never
    /// mentions the variable and solves fine.
    /// </remarks>
    protected override bool AllowsPartialInterface => false;

    /// <inheritdoc/>
    protected override bool IsLinkTarget(IGH_DocumentObject candidate) =>
        GhPythonBridge.IsCSharpComponent(candidate);

    /// <inheritdoc/>
    /// <remarks>
    /// Parses the CSharpComponent JSON, checks the source against its own declared parameters,
    /// pushes code/inputs/outputs to the linked target, and expires it so it re-solves before the
    /// read pass. Parse, link, or signature failures are stashed and surfaced in
    /// <see cref="ReadSolve"/> — nothing is pushed in those cases, so the target keeps whatever it
    /// last ran.
    /// </remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        _pushError = null;
        _rejection = null;

        if (!ScriptComponentJson.TryParse(data, out string code, out List<GhParamSpec> inputs, out List<GhParamSpec> outputs, out string parseError))
        {
            _pushError = $"Could not parse CSharpComponent JSON: {parseError}";
            return;
        }

        IGH_DocumentObject? target = ResolveTarget(out string? linkError);
        if (target is null)
        {
            _pushError = linkError;
            return;
        }

        // An enabled Script I/O linked to this transmitter freezes the target's interface: the
        // declared parameters must be exactly the ones already on the component (see
        // AllowsPartialInterface), and only the code is pushed — the params are never restructured,
        // so the wires into and out of the component survive every push. The signature check still
        // runs, which is what closes the loop: declared == locked and signature == declared, so the
        // code the target ends up with binds to the parameters it already has.
        bool locked = ActiveScriptIO is not null;
        if (locked)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Interface locked — pushing code only; the target's inputs/outputs are preserved.");

            if (!RespectsLockedInterface(target, inputs, outputs, out string lockFeedback))
            {
                _rejection = lockFeedback;
                return;
            }
        }

        if (!TryCheckSignature(code, inputs, outputs, out string rejection))
        {
            _rejection = rejection;
            return;
        }

        GhPythonBridge.SetScript(target, code);

        if (!locked)
        {
            if (inputs.Count > 0)
                GhPythonBridge.SetInputs(target, inputs);
            if (outputs.Count > 0)
                GhPythonBridge.SetOutputs(target, outputs);
        }

        GhPythonBridge.Expire(target);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Defers the read pass until the linked target has actually re-solved, so the runtime messages
    /// read in <see cref="ReadSolve"/> reflect the pushed code rather than a still-expired (empty)
    /// state. Anything that stopped the push reads immediately — there is nothing to wait for.
    /// </remarks>
    protected override bool IsReadReady(string data)
    {
        if (_pushError != null || _rejection != null)
            return true;

        IGH_DocumentObject? target = ResolveTarget(out _);
        return target is null || GhPythonBridge.HasComputed(target);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the target's fresh messages after its push-triggered re-solve and routes Success (the
    /// linked component's GUID) or Fail (the error text) accordingly. Unlike the Python side there
    /// is no unconnected-input filter to apply: C# hands an unwired input its default rather than
    /// raising a recognisable name error, so an empty input surfaces as an ordinary exception. The
    /// feedback names those inputs instead, so the model is not sent chasing a bug in its code that
    /// is really a missing wire.
    /// </remarks>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        if (_pushError != null)
            return RoutingResult.Fail(_pushError, _pushError, GH_RuntimeMessageLevel.Error);

        if (_rejection != null)
            return RoutingResult.Fail(_rejection, "Submission rejected — code not applied.", GH_RuntimeMessageLevel.Warning);

        IGH_DocumentObject? target = ResolveTarget(out string? linkError);
        if (target is null)
            return RoutingResult.Fail(linkError ?? "Linked C# component unavailable.", linkError, GH_RuntimeMessageLevel.Error);

        IReadOnlyList<string> errors = GhPythonBridge.GetErrors(target);

        return errors.Count > 0
            ? RoutingResult.Fail(BuildFeedback(errors, UnconnectedInputs(target)), "Target C# reported errors.", GH_RuntimeMessageLevel.Warning)
            : RoutingResult.Ok(LinkedGuid.ToString());
    }

    /// <summary>
    /// Checks the source against the parameters the same submission declares: the script-instance
    /// class must be present, it must carry a <c>RunScript</c> method, and that method's signature
    /// must name exactly the declared inputs by value and the declared outputs by <c>out</c>.
    ///
    /// <para>This is the one defect a C# submission can carry that neither the schema nor the
    /// compiler catches usefully: parameters that disagree bind nothing, and the component fails
    /// with a message about the signature rather than about the mistake. Caught here it costs one
    /// round with the expected signature written out.</para>
    /// </summary>
    /// <param name="code">The submitted C# source.</param>
    /// <param name="inputs">The submission's declared input specs.</param>
    /// <param name="outputs">The submission's declared output specs.</param>
    /// <param name="rejection">Corrective feedback for the model when the check fails.</param>
    /// <returns>true when the source and the declared parameters agree.</returns>
    private static bool TryCheckSignature(
        string code,
        IReadOnlyList<GhParamSpec> inputs,
        IReadOnlyList<GhParamSpec> outputs,
        out string rejection)
    {
        rejection = string.Empty;

        if (!ScriptInstanceClass.IsMatch(code))
        {
            rejection = BuildRejection(
                "the source does not declare `public class Script_Instance : GH_ScriptInstance`",
                inputs,
                outputs);
            return false;
        }

        Match signature = RunScriptSignature.Match(code);
        if (!signature.Success)
        {
            rejection = BuildRejection(
                "the source has no `private void RunScript(...)` method for Grasshopper to call",
                inputs,
                outputs);
            return false;
        }

        (List<string> byValue, List<string> byRef) = ParseSignature(signature.Groups["params"].Value);

        var declaredInputs = inputs.Select(p => p.Name).ToList();
        var declaredOutputs = outputs.Select(p => p.Name).ToList();

        List<string> problems = new();
        Describe(problems, "input", declaredInputs, byValue);
        Describe(problems, "output", declaredOutputs, byRef);

        if (problems.Count == 0)
            return true;

        rejection = BuildRejection(string.Join("; ", problems), inputs, outputs);
        return false;
    }

    /// <summary>
    /// Splits a RunScript parameter list into the names passed by value (the component's inputs) and
    /// the names passed by <c>out</c>/<c>ref</c> (its outputs). Commas inside a generic argument list
    /// are not separators, so the split tracks angle-bracket depth.
    /// </summary>
    /// <param name="parameterList">The text between the RunScript parentheses.</param>
    /// <returns>The by-value names and the by-reference names, in signature order.</returns>
    private static (List<string> ByValue, List<string> ByRef) ParseSignature(string parameterList)
    {
        var byValue = new List<string>();
        var byRef = new List<string>();

        foreach (string parameter in SplitParameters(parameterList))
        {
            string[] words = parameter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
                continue;

            string name = words[^1];
            bool isOut = words[0].Equals("out", StringComparison.Ordinal)
                || words[0].Equals("ref", StringComparison.Ordinal);

            (isOut ? byRef : byValue).Add(name);
        }

        return (byValue, byRef);
    }

    /// <summary>
    /// Splits a parameter list on the commas that separate parameters, ignoring those nested inside
    /// generic arguments (<c>Dictionary&lt;string, int&gt;</c> is one parameter, not two).
    /// </summary>
    /// <param name="parameterList">The text between the RunScript parentheses.</param>
    /// <returns>One entry per declared parameter.</returns>
    private static IEnumerable<string> SplitParameters(string parameterList)
    {
        int depth = 0;
        int start = 0;

        for (int i = 0; i < parameterList.Length; i++)
        {
            char c = parameterList[i];
            if (c == '<')
                depth++;
            else if (c == '>')
                depth--;
            else if (c == ',' && depth == 0)
            {
                yield return parameterList[start..i];
                start = i + 1;
            }
        }

        if (start < parameterList.Length)
            yield return parameterList[start..];
    }

    /// <summary>
    /// Adds a description of how one side of the interface disagrees — names declared but absent
    /// from the signature, and names in the signature nobody declared.
    /// </summary>
    /// <param name="problems">The running list of problems.</param>
    /// <param name="kind">"input" or "output", for the message.</param>
    /// <param name="declared">The names declared in the submission.</param>
    /// <param name="inSignature">The names found in that position of the signature.</param>
    private static void Describe(List<string> problems, string kind, IReadOnlyList<string> declared, IReadOnlyList<string> inSignature)
    {
        List<string> missing = declared.Where(n => !inSignature.Contains(n, StringComparer.Ordinal)).ToList();
        List<string> extra = inSignature.Where(n => !declared.Contains(n, StringComparer.Ordinal)).ToList();

        if (missing.Count > 0)
            problems.Add($"declared {kind}s missing from the signature: {string.Join(", ", missing)}");

        if (extra.Count > 0)
            problems.Add($"signature {kind}s you never declared: {string.Join(", ", extra)}");
    }

    /// <summary>
    /// Builds the corrective feedback for a rejected submission: what is wrong, then the exact
    /// RunScript signature the declared parameters call for, so the fix is a copy rather than a
    /// guess.
    /// </summary>
    /// <param name="problem">What disagreed.</param>
    /// <param name="inputs">The submission's declared input specs.</param>
    /// <param name="outputs">The submission's declared output specs.</param>
    /// <returns>The feedback text.</returns>
    private static string BuildRejection(string problem, IReadOnlyList<GhParamSpec> inputs, IReadOnlyList<GhParamSpec> outputs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Your submission was REJECTED and nothing was applied: the C# source and the parameters you declared do not agree.");
        sb.AppendLine();
        sb.AppendLine($"Problem: {problem}.");
        sb.AppendLine();
        sb.AppendLine("The parameters you declared call for exactly this entry point:");
        sb.AppendLine();
        sb.AppendLine($"    private void RunScript({ExpectedParameters(inputs, outputs)})");
        sb.AppendLine();
        sb.Append("Resubmit the full CSharpComponent JSON with that signature inside `public class Script_Instance : GH_ScriptInstance`, keeping the declared names and access exactly as they are.");

        return sb.ToString();
    }

    /// <summary>
    /// Renders the RunScript parameter list the declared specs call for: inputs by value in their
    /// hinted C# type, outputs by <c>out</c>, each wrapped in <c>List&lt;&gt;</c> or
    /// <c>DataTree&lt;&gt;</c> to match its access.
    /// </summary>
    /// <param name="inputs">The declared input specs.</param>
    /// <param name="outputs">The declared output specs.</param>
    /// <returns>The parameter list, comma separated.</returns>
    private static string ExpectedParameters(IReadOnlyList<GhParamSpec> inputs, IReadOnlyList<GhParamSpec> outputs)
    {
        IEnumerable<string> parts = inputs
            .Select(p => $"{Wrap(TypeName(p.TypeHint), p.Access)} {p.Name}")
            .Concat(outputs.Select(p => $"out {Wrap("object", p.Access)} {p.Name}"));

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The C# type a Physalia type hint must be spelled as. An empty or unknown hint is an untyped
    /// parameter, which arrives as <c>object</c>.
    /// </summary>
    /// <param name="typeHint">The Physalia type-hint name.</param>
    /// <returns>The C# type name.</returns>
    private static string TypeName(string typeHint)
        => CSharpTypeNames.TryGetValue(typeHint ?? string.Empty, out string? name) ? name : "object";

    /// <summary>
    /// Wraps a C# type in the collection its access calls for, which is also how the engine reads
    /// the access back off the signature.
    /// </summary>
    /// <param name="typeName">The element type name.</param>
    /// <param name="access">The declared access.</param>
    /// <returns>The parameter's C# type name.</returns>
    private static string Wrap(string typeName, GhScriptParamAccess access) => access switch
    {
        GhScriptParamAccess.List => $"List<{typeName}>",
        GhScriptParamAccess.Tree => $"DataTree<{typeName}>",
        _ => typeName,
    };

    /// <summary>
    /// Names the target's inputs that currently have nothing to read — no source and no data. A C#
    /// script is handed the default for those (null for a reference type), so they are the usual
    /// cause of an exception in code that is otherwise correct.
    /// </summary>
    /// <param name="target">The linked C# Script component.</param>
    /// <returns>The unconnected input names.</returns>
    private static IReadOnlyList<string> UnconnectedInputs(IGH_DocumentObject target)
    {
        if (target is not IGH_Component component)
            return Array.Empty<string>();

        return component.Params.Input
            .Where(p => p.SourceCount == 0 && p.VolatileData.IsEmpty)
            .Select(p => p.Name)
            .ToList();
    }

    private static string BuildFeedback(IReadOnlyList<string> errors, IReadOnlyList<string> unconnected)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The C# code you generated produced errors. Please fix and resubmit.");
        sb.AppendLine();
        sb.AppendLine("Errors:");
        foreach (string error in errors)
            sb.AppendLine($"  - {error}");

        if (unconnected.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"Note: these inputs are unconnected on the canvas, so your code received their default value (null for reference types): {string.Join(", ", unconnected)}. "
                + "If an error above comes from one of them, make the code tolerate missing input rather than changing the interface.");
        }

        return sb.ToString().TrimEnd();
    }
}
