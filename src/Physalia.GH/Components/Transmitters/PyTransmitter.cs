// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.Core.Python;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// Takes LLM-generated Python (validated against PythonComponent.json, arriving as the
/// consumed signal's payload) and pushes its code, inputs, and outputs into a linked GH
/// Python Script component, then reads back the target's runtime errors. On clean
/// execution it routes the linked Python component's GUID forward on the Success Signal
/// (so a downstream Runtime Health Check or Geometry Observation can scope to it); on genuine errors it
/// routes the messages back on the Fail Signal. Errors caused purely by unconnected
/// inputs are ignored. Link to the target by dragging the harness proxy's "py" grip onto it,
/// or from this node's right-click picker.
/// When an enabled <see cref="ScriptIO"/> is linked to this transmitter, the target's
/// interface is frozen: pushes carry code only (parameters are never restructured, so existing
/// wires survive), and a submission declaring parameters outside the locked set is rejected with
/// corrective feedback on the Fail Signal.
/// </summary>
public class PyTransmitter : ScriptTransmitterBase
{
    private string? _pushError;
    private string? _lockFeedback;

    // Parsed params from the current push, retained so they can be re-applied once the target
    // has solved (see ReapplyAccessIfNeeded) to defeat the first-push access clobber.
    private List<GhParamSpec> _inputs = new();
    private List<GhParamSpec> _outputs = new();
    private bool _accessReapplied;

    /// <summary>
    /// Initializes a new instance of the <see cref="PyTransmitter"/> class.
    /// </summary>
    public PyTransmitter()
        : base(
            "Py Transmitter",
            "PyTx",
            "Pushes LLM-generated Python into a linked GH Python Script component and routes its errors. Drag the harness's \"py\" grip to the target Python component.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("8E3B1C7A-2F4D-4A19-9C6E-0B5D7A2E1F38");

    /// <inheritdoc/>
    public override string OutletLabel => "py";

    /// <inheritdoc/>
    public override WireGradient OutletGradient => ArrowStyles.PyTransmitter;

    /// <inheritdoc/>
    public override ScriptInterfaceDialect Dialect => ScriptInterfaceDialect.Python;

    /// <inheritdoc/>
    protected override string TargetKind => "Python Script";

    /// <inheritdoc/>
    /// <remarks>
    /// Python 3 only. Every Rhino 8 script component wears the same interface, so without the
    /// language test this would happily link to the C# component next door and push Python into it
    /// — and <see cref="CsTransmitter"/> would do the reverse.
    /// </remarks>
    protected override bool IsLinkTarget(IGH_DocumentObject candidate) =>
        GhPythonBridge.IsPython3Component(candidate);

    /// <inheritdoc/>
    /// <remarks>
    /// Parses the PythonComponent JSON, pushes code/inputs/outputs to the linked target,
    /// and expires it so it re-solves before the read pass. Parse or link failures are
    /// stashed and surfaced in <see cref="ReadSolve"/>.
    /// </remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        _pushError = null;
        _lockFeedback = null;
        _accessReapplied = false;
        _inputs = new List<GhParamSpec>();
        _outputs = new List<GhParamSpec>();

        if (!ScriptComponentJson.TryParse(data, out string code, out List<GhParamSpec> inputs, out List<GhParamSpec> outputs, out string parseError))
        {
            _pushError = $"Could not parse PythonComponent JSON: {parseError}";
            return;
        }

        // Python declares its parameters only in the submission — nothing in the code says what
        // access an output has — so the access is corrected against the code here, before the push.
        outputs = PromoteListOutputs(code, outputs);

        IGH_DocumentObject? target = ResolveTarget(out string? linkError);
        if (target is null)
        {
            _pushError = linkError;
            return;
        }

        // An enabled Script I/O linked to this transmitter freezes the target's interface:
        // push the code only — never restructure parameters, re-hint outputs, or touch the
        // marshalling flag, so the target keeps exactly the params (and wires) it already has.
        // A submission declaring names outside the locked set is rejected before anything is
        // pushed, and the contract is routed back as corrective feedback. _inputs/_outputs stay
        // empty so the access re-apply pass in IsReadReady never runs.
        if (ActiveScriptIO is not null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Interface locked — pushing code only; the target's inputs/outputs are preserved.");

            if (!RespectsLockedInterface(target, inputs, outputs, out string lockFeedback))
            {
                _lockFeedback = lockFeedback;
                return;
            }

            GhPythonBridge.SetScript(target, code);

            // The parameter SET is frozen, but a hint or access correction is applied in place —
            // the whole point of telling the model what is actually arriving on the wires.
            ApplyLockedInterfaceAdjustments(target, inputs, outputs);

            GhPythonBridge.Expire(target);
            return;
        }

        _inputs = inputs;
        _outputs = outputs;

        GhPythonBridge.SetScript(target, code);
        if (inputs.Count > 0)
            GhPythonBridge.SetInputs(target, inputs);
        if (outputs.Count > 0)
        {
            GhPythonBridge.SetOutputs(target, outputs);

            // Pin outputs to No Type Hint: SetOutputs leaves them on the engine's Default Python Hint
            // (often "ghdoc Object"), which wraps any Python value — a list especially — as one opaque
            // goo even under List access. No Type Hint lets a list flatten under List access.
            GhPythonBridge.SetOutputsNoTypeHint(target, outputs.Select(o => o.Name));
        }

        // Turn marshalling on so Python list outputs are converted to a .NET list and the engine
        // expands them into individual items. With marshalling off (which a pushed script can land in)
        // the raw Python object is wrapped as one opaque goo, the core symptom of this whole fix.
        GhPythonBridge.EnableOutputMarshalling(target);

        GhPythonBridge.Expire(target);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Defers the read pass until the linked target has actually re-solved, so the
    /// runtime messages read in <see cref="ReadSolve"/> reflect the pushed code rather
    /// than a still-expired (empty) state. Push/link failures read immediately — there
    /// is nothing to wait for.
    /// </remarks>
    protected override bool IsReadReady(string data)
    {
        if (_pushError != null || _lockFeedback != null)
            return true;

        IGH_DocumentObject? target = ResolveTarget(out _);
        if (target is null)
            return true;

        if (!GhPythonBridge.HasComputed(target))
            return false;

        // First-push access clobber: the GH Python Script component auto-declares its
        // parameters item-access whenever it has no compiled instance yet, which is the case on
        // the first push of every freshly generated script. That silently overrides the list/tree
        // access set by SetOutputs, so a Python list assigned to an output is wrapped as one
        // opaque object on the canvas (the wire then fails "Goo to Curve" downstream). Now that
        // the target has solved once an instance exists, so re-applying the access *in place*
        // (without restructuring the parameter set, which would re-invalidate the instance and
        // re-trigger the clobber) makes the engine honour it. Re-solve once so it takes effect.
        if (!_accessReapplied && NeedsAccessReapply())
        {
            GhPythonBridge.ApplyInputAccess(target, BuildAccessMap(_inputs));
            GhPythonBridge.ApplyOutputAccess(target, BuildAccessMap(_outputs));
            GhPythonBridge.Expire(target);
            _accessReapplied = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether any pushed parameter declares non-item (list or tree) access, which is the access
    /// the first-push clobber resets to item and which therefore needs the corrective re-apply.
    /// Item-only components solve correctly on the first push and skip the extra solve.
    /// </summary>
    /// <returns>true if a re-apply pass is warranted.</returns>
    private bool NeedsAccessReapply()
        => _outputs.Exists(o => o.Access != GhScriptParamAccess.Item)
           || _inputs.Exists(i => i.Access != GhScriptParamAccess.Item);

    /// <summary>
    /// Builds a variable-name to access map from the parsed specs, for the in-place access re-apply.
    /// </summary>
    /// <param name="specs">The parsed parameter specs.</param>
    /// <returns>A map of variable name to declared access.</returns>
    private static Dictionary<string, GhScriptParamAccess> BuildAccessMap(IReadOnlyList<GhParamSpec> specs)
    {
        var map = new Dictionary<string, GhScriptParamAccess>(StringComparer.Ordinal);
        foreach (GhParamSpec spec in specs)
            map[spec.Name] = spec.Access;

        return map;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the target's fresh runtime errors (after its push-triggered re-solve),
    /// filters out unconnected-input complaints, and routes Success (the linked component's
    /// GUID) or Fail (the error text) accordingly.
    /// </remarks>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        if (_pushError != null)
            return RoutingResult.Fail(_pushError, _pushError, GH_RuntimeMessageLevel.Error);

        if (_lockFeedback != null)
            return RoutingResult.Fail(_lockFeedback, "Locked interface violated — code not applied.", GH_RuntimeMessageLevel.Warning);

        IGH_DocumentObject? target = ResolveTarget(out string? linkError);
        if (target is null)
            return RoutingResult.Fail(linkError ?? "Linked Python component unavailable.", linkError, GH_RuntimeMessageLevel.Error);

        List<string> realErrors = GhPythonBridge.GetErrors(target)
            .Where(message => !IsInputConnectionError(message, target))
            .ToList();

        return realErrors.Count > 0
            ? RoutingResult.Fail(BuildFeedback(realErrors), "Target Python reported errors.", GH_RuntimeMessageLevel.Warning)
            : RoutingResult.Ok(LinkedGuid.ToString());
    }

    /// <summary>
    /// Corrects output access against the code: any output the script assigns a Python list but that
    /// was declared (or defaulted) to <c>item</c> access is promoted to <c>list</c>. An item-access
    /// output handed a list wraps the whole list as one opaque object on the canvas, unreadable by
    /// downstream components — this is the deterministic guard against that common model slip.
    /// Outputs declared <c>tree</c> are left untouched.
    /// </summary>
    /// <param name="code">The component's Python source.</param>
    /// <param name="outputs">The parsed output specs.</param>
    /// <returns>The output specs with list-valued item outputs promoted to list access.</returns>
    private static List<GhParamSpec> PromoteListOutputs(string code, List<GhParamSpec> outputs)
    {
        if (outputs.Count == 0)
        {
            return outputs;
        }

        IReadOnlyCollection<string> listOutputs = PythonOutputAccessInference.InferListVariables(
            code, outputs.Select(o => o.Name));
        if (listOutputs.Count == 0)
        {
            return outputs;
        }

        for (int i = 0; i < outputs.Count; i++)
        {
            if (outputs[i].Access == GhScriptParamAccess.Item && listOutputs.Contains(outputs[i].Name))
            {
                outputs[i] = outputs[i] with { Access = GhScriptParamAccess.List };
            }
        }

        return outputs;
    }

    /// <summary>
    /// Determines whether a runtime message is an unconnected-input complaint rather than
    /// a genuine code error. Only the specific Python shape <c>name 'x' is not defined</c>,
    /// where <c>x</c> is a target input that currently has no upstream source or data, is
    /// ignored — everything else is treated as a real error and routed back as Feedback.
    /// </summary>
    /// <param name="message">The runtime message from the target.</param>
    /// <param name="target">The linked Python Script component.</param>
    /// <returns>true if the message should be ignored as an input-connection error.</returns>
    private static bool IsInputConnectionError(string message, IGH_DocumentObject target)
    {
        if (string.IsNullOrWhiteSpace(message))
            return true;

        if (target is not IGH_Component component)
            return false;

        foreach (IGH_Param param in component.Params.Input)
        {
            if (param.SourceCount > 0 && !param.VolatileData.IsEmpty)
                continue;

            string escaped = Regex.Escape(param.Name);
            if (Regex.IsMatch(message, $@"name\s+['""]{escaped}['""]\s+is\s+not\s+defined", RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    private static string BuildFeedback(IReadOnlyList<string> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The Python code you generated produced runtime errors. Please fix and resubmit.");
        sb.AppendLine();
        sb.AppendLine("Errors:");
        foreach (string error in errors)
            sb.AppendLine($"  - {error}");

        return sb.ToString().TrimEnd();
    }
}
