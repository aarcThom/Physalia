// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Grounding;
using Physalia.Core.Python;
using Physalia.Core.Signals;
using Physalia.GH.Attributes;
using Physalia.GH.Generation;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Takes LLM-generated Python (validated against PythonComponent.json, arriving as the
/// consumed signal's payload) and pushes its code, inputs, and outputs into a linked GH
/// Python Script component, then reads back the target's runtime errors. On clean
/// execution it routes the linked Python component's GUID forward on the Success Signal
/// (so a downstream Runtime Health Check or Geometry Observation can scope to it); on genuine errors it
/// routes the messages back on the Fail Signal. Errors caused purely by unconnected
/// inputs are ignored. Link to the target via the bottom-centre bezier grip.
/// When an enabled <see cref="InterfaceLock"/> is linked to this transmitter, the target's
/// interface is frozen: pushes carry code only (parameters are never restructured, so existing
/// wires survive), and a submission declaring parameters outside the locked set is rejected with
/// corrective feedback on the Fail Signal.
/// </summary>
public class PyTransmitter : RoutingComponentBase<string>, IHarnessArrow
{
    private Guid _linkedGuid = Guid.Empty;
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
            "Pushes LLM-generated Python into a linked GH Python Script component and routes its errors. Drag the bottom grip to the target Python component.",
            "Transmitters")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("8E3B1C7A-2F4D-4A19-9C6E-0B5D7A2E1F38");

    /// <summary>
    /// Gets the InstanceGuid of the linked GH Python Script component, or <see cref="Guid.Empty"/> if unlinked.
    /// </summary>
    public Guid LinkedGuid => _linkedGuid;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        // A plain node: the drag arrow now lives on the harness proxy, which sits on the same canvas
        // as the script components this transmitter targets (see IHarnessArrow).
        m_attributes = new PhyComponentAttributes(this);
    }

    /// <summary>
    /// Links this component to a GH Python Script component.
    /// Called from the right-click picker, and by the harness proxy's delegated arrow on drop.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the Python Script component to link.</param>
    public void LinkTo(Guid guid)
    {
        _linkedGuid = guid;
    }

    /// <summary>
    /// Removes the current link. Does not modify the previously-linked component's code.
    /// Called from the right-click picker, and by the harness proxy's delegated arrow on Ctrl+drop.
    /// </summary>
    public void Unlink()
    {
        _linkedGuid = Guid.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>The wire lands just under the linked script component.</remarks>
    IEnumerable<PointF> IHarnessArrow.GetArrowEndpoints(GH_Document hostDocument)
    {
        if (_linkedGuid != Guid.Empty && hostDocument.FindObject(_linkedGuid, false) is { } target)
        {
            RectangleF b = target.Attributes.Bounds;
            yield return new PointF(b.Left + (b.Width / 2f), b.Bottom + 6f);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Links the script component under the drop point; Ctrl unlinks instead.</remarks>
    void IHarnessArrow.HandleDrop(GH_Document hostDocument, PointF dropPoint, bool ctrl)
    {
        foreach (IGH_DocumentObject obj in hostDocument.Objects)
        {
            if (obj.Attributes.Bounds.Contains(dropPoint) && GhPythonBridge.IsScriptComponent(obj))
            {
                SetLink(ctrl ? Guid.Empty : obj.InstanceGuid);
                return;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Offers the grip link as a menu instead of a drag. A transmitter normally lives inside a
    /// harness while its script component sits on the user's canvas, and a drag cannot cross two
    /// canvases — so the link has to be pickable from a list of the host document's script
    /// components. The bottom-grip drag still works when both happen to share a document.
    /// </remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);

        ToolStripMenuItem picker = Menu_AppendItem(menu, "Link to Script Component");

        List<IGH_DocumentObject> candidates = PhyDocuments.Host(this) is { } host
            ? host.Objects.Where(GhPythonBridge.IsScriptComponent).ToList()
            : new List<IGH_DocumentObject>();

        if (candidates.Count == 0)
        {
            Menu_AppendItem(picker.DropDown, "No script components on the canvas", (_, _) => { }, false);
        }
        else
        {
            foreach (IGH_DocumentObject candidate in candidates)
            {
                Guid guid = candidate.InstanceGuid;
                Menu_AppendItem(
                    picker.DropDown,
                    $"{candidate.NickName}  ({guid.ToString()[..8]})",
                    (_, _) => SetLink(guid),
                    enabled: true,
                    @checked: guid == _linkedGuid);
            }
        }

        Menu_AppendItem(menu, "Unlink Script Component", (_, _) => SetLink(Guid.Empty), _linkedGuid != Guid.Empty);
    }

    // Applies a link change from the menu: records undo so the pick can be reversed, then re-solves
    // so the target and any Interface Lock watching it pick the change up.
    private void SetLink(Guid guid)
    {
        RecordUndoEvent(guid == Guid.Empty ? "Unlink Script Component" : "Link Script Component");

        if (guid == Guid.Empty)
        {
            Unlink();
        }
        else
        {
            LinkTo(guid);
        }

        ExpireSolution(true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The validated PythonComponent JSON ({ code, inputs[], outputs[] }) arrives as the
    /// consumed signal's payload (Schema Validator's Success Signal).
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

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

        if (!TryParse(data, out string code, out List<GhParamSpec> inputs, out List<GhParamSpec> outputs, out string parseError))
        {
            _pushError = $"Could not parse PythonComponent JSON: {parseError}";
            return;
        }

        IGH_DocumentObject? target = ResolveTarget(out string? linkError);
        if (target is null)
        {
            _pushError = linkError;
            return;
        }

        // An enabled Interface Lock linked to this transmitter freezes the target's interface:
        // push the code only — never restructure parameters, re-hint outputs, or touch the
        // marshalling flag, so the target keeps exactly the params (and wires) it already has.
        // A submission declaring names outside the locked set is rejected before anything is
        // pushed, and the contract is routed back as corrective feedback. _inputs/_outputs stay
        // empty so the access re-apply pass in IsReadReady never runs.
        if (FindInterfaceLock() is not null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Interface locked — pushing code only; the target's inputs/outputs are preserved.");

            if (!ValidateAgainstLockedInterface(target, inputs, outputs, out string lockFeedback))
            {
                _lockFeedback = lockFeedback;
                return;
            }

            GhPythonBridge.SetScript(target, code);
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
            : RoutingResult.Ok(_linkedGuid.ToString());
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetGuid("LinkedGuid", _linkedGuid);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("LinkedGuid"))
            _linkedGuid = reader.GetGuid("LinkedGuid");
        return base.Read(reader);
    }

    /// <summary>
    /// Resolves the linked GH Python Script component, or returns null with a message
    /// when no valid target is linked.
    /// </summary>
    /// <param name="error">A human-readable reason when resolution fails; otherwise null.</param>
    /// <returns>The linked document object, or null.</returns>
    private IGH_DocumentObject? ResolveTarget(out string? error)
    {
        error = null;

        if (_linkedGuid == Guid.Empty)
        {
            error = "No Python Script component linked. Drag from the bottom grip to connect, or use "
                + "\"Link to Script Component\" on this node's right-click menu.";
            return null;
        }

        // The script component lives on the user's canvas, not in the harness this transmitter runs in.
        IGH_DocumentObject? linked = Harness.PhyDocuments.Host(this)?.FindObject(_linkedGuid, false);
        if (linked is null || !GhPythonBridge.IsScriptComponent(linked))
        {
            error = "Linked component not found or is not a Python Script component.";
            return null;
        }

        return linked;
    }

    /// <summary>
    /// Finds the Interface Lock actively constraining this transmitter: an enabled
    /// <see cref="InterfaceLock"/> anywhere in the document whose grip link points at this
    /// component. Null when the interface is free to restructure.
    /// </summary>
    /// <returns>The constraining lock, or null.</returns>
    private InterfaceLock? FindInterfaceLock()
        => OnPingDocument()?.Objects.OfType<InterfaceLock>().FirstOrDefault(l => l.Constrains(InstanceGuid));

    /// <summary>
    /// Validates a submission against the target's live (locked) interface: every declared input
    /// and output name must already exist on the target. Declaring fewer parameters than exist is
    /// fine — the params are never rebuilt, so nothing is removed — but an unknown name means the
    /// model tried to add or rename a parameter, which would break existing wires.
    /// </summary>
    /// <param name="target">The linked Python Script component.</param>
    /// <param name="inputs">The submission's declared input specs.</param>
    /// <param name="outputs">The submission's declared output specs.</param>
    /// <param name="feedback">Corrective feedback for the model when validation fails.</param>
    /// <returns>true when the submission respects the locked interface.</returns>
    private static bool ValidateAgainstLockedInterface(
        IGH_DocumentObject target,
        IReadOnlyList<GhParamSpec> inputs,
        IReadOnlyList<GhParamSpec> outputs,
        out string feedback)
    {
        feedback = string.Empty;

        IReadOnlyList<GhParamSpec> lockedInputs = GhPythonBridge.GetInputSpecs(target);
        IReadOnlyList<GhParamSpec> lockedOutputs = GhPythonBridge.GetOutputSpecs(target);

        var inputNames = new HashSet<string>(lockedInputs.Select(p => p.Name), StringComparer.Ordinal);
        var outputNames = new HashSet<string>(lockedOutputs.Select(p => p.Name), StringComparer.Ordinal);

        List<string> unknownInputs = inputs.Select(i => i.Name).Where(n => !inputNames.Contains(n)).ToList();
        List<string> unknownOutputs = outputs.Select(o => o.Name).Where(n => !outputNames.Contains(n)).ToList();

        if (unknownInputs.Count == 0 && unknownOutputs.Count == 0)
            return true;

        feedback = BuildLockFeedback(target.NickName, lockedInputs, lockedOutputs, unknownInputs, unknownOutputs);
        return false;
    }

    /// <summary>
    /// Builds the corrective feedback for a locked-interface violation: what was rejected and why,
    /// then the locked contract rendered exactly as the grounding renders it, so the model sees the
    /// same JSON entries it must copy on the resubmission.
    /// </summary>
    /// <param name="componentName">The target script component's display name.</param>
    /// <param name="lockedInputs">The target's live input specs.</param>
    /// <param name="lockedOutputs">The target's live output specs.</param>
    /// <param name="unknownInputs">Declared input names not on the target.</param>
    /// <param name="unknownOutputs">Declared output names not on the target.</param>
    /// <returns>The feedback text.</returns>
    private static string BuildLockFeedback(
        string componentName,
        IReadOnlyList<GhParamSpec> lockedInputs,
        IReadOnlyList<GhParamSpec> lockedOutputs,
        IReadOnlyList<string> unknownInputs,
        IReadOnlyList<string> unknownOutputs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Your submission was REJECTED and nothing was applied: it declares parameters that do not exist on the locked component.");
        if (unknownInputs.Count > 0)
            sb.AppendLine($"Unknown inputs: {string.Join(", ", unknownInputs)}");
        if (unknownOutputs.Count > 0)
            sb.AppendLine($"Unknown outputs: {string.Join(", ", unknownOutputs)}");
        sb.AppendLine();

        var contract = new ScriptInterfaceGrounding(
            componentName,
            InterfaceLock.ToPorts(lockedInputs),
            InterfaceLock.ToPorts(lockedOutputs));
        sb.AppendLine(contract.ToSystemPromptSection());
        sb.AppendLine();
        sb.Append("Resubmit the full PythonComponent JSON declaring exactly these parameters, with the code written against these variable names.");

        return sb.ToString();
    }

    /// <summary>
    /// Parses a PythonComponent JSON object into code plus typed input/output specs.
    /// </summary>
    /// <param name="json">The JSON string from the Data input.</param>
    /// <param name="code">The parsed Python source.</param>
    /// <param name="inputs">The parsed input parameter specs.</param>
    /// <param name="outputs">The parsed output parameter specs.</param>
    /// <param name="error">A parse error message when the result is false.</param>
    /// <returns>true if the JSON parsed and contained non-empty code; otherwise false.</returns>
    private static bool TryParse(string json, out string code, out List<GhParamSpec> inputs, out List<GhParamSpec> outputs, out string error)
    {
        code = string.Empty;
        inputs = new List<GhParamSpec>();
        outputs = new List<GhParamSpec>();
        error = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Root JSON value must be an object.";
                return false;
            }

            if (root.TryGetProperty("code", out JsonElement codeEl) && codeEl.ValueKind == JsonValueKind.String)
                code = codeEl.GetString() ?? string.Empty;

            inputs = ParseParams(root, "inputs");
            outputs = PromoteListOutputs(code, ParseParams(root, "outputs"));

            if (!StringHelpers.IsNonBlank(code))
            {
                error = "Missing or empty 'code' field.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Reads a JSON array of <c>{ name, type, access }</c> parameter objects into specs.
    /// Entries missing a non-blank name are skipped; type defaults to untyped and access to item.
    /// </summary>
    /// <param name="root">The root JSON object.</param>
    /// <param name="propertyName">The array property to read (e.g. "inputs").</param>
    /// <returns>The parsed parameter specs.</returns>
    private static List<GhParamSpec> ParseParams(JsonElement root, string propertyName)
    {
        var specs = new List<GhParamSpec>();

        if (!root.TryGetProperty(propertyName, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            return specs;

        foreach (JsonElement el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;

            if (!el.TryGetProperty("name", out JsonElement nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;

            string name = nameEl.GetString() ?? string.Empty;
            if (!StringHelpers.IsNonBlank(name))
                continue;

            string typeHint = el.TryGetProperty("type", out JsonElement typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString() ?? string.Empty
                : string.Empty;

            string access = el.TryGetProperty("access", out JsonElement accessEl) && accessEl.ValueKind == JsonValueKind.String
                ? accessEl.GetString() ?? "item"
                : "item";

            specs.Add(new GhParamSpec(name, typeHint, MapAccess(access)));
        }

        return specs;
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
    /// Maps a JSON access string (<c>item</c>, <c>list</c>, <c>tree</c>) to the access enum.
    /// </summary>
    /// <param name="access">The access string; unknown values default to item.</param>
    /// <returns>The mapped access mode.</returns>
    private static GhScriptParamAccess MapAccess(string access) => access.ToLowerInvariant() switch
    {
        "list" => GhScriptParamAccess.List,
        "tree" => GhScriptParamAccess.Tree,
        _ => GhScriptParamAccess.Item,
    };

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
