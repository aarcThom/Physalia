// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.GH.Attributes;
using Physalia.GH.Generation;

namespace Physalia.GH.Components.GhPython;

/// <summary>
/// Takes LLM-generated Python (validated against PythonComponent.json, arriving as the
/// consumed signal's payload) and pushes its code, inputs, and outputs into a linked GH
/// Python Script component, then reads back the target's runtime errors. On clean
/// execution it routes the code forward on the Success Signal; on genuine errors it
/// routes the messages back on the Fail Signal. Errors caused purely by unconnected
/// inputs are ignored. Link to the target via the bottom-centre bezier grip.
/// </summary>
public class PyTransmitter : RoutingComponentBase<string>
{
    private Guid _linkedGuid = Guid.Empty;
    private string _pendingCode = string.Empty;
    private string? _pushError;

    /// <summary>
    /// Initializes a new instance of the <see cref="PyTransmitter"/> class.
    /// </summary>
    public PyTransmitter()
        : base(
            "Py Transmitter",
            "PyTx",
            "Pushes LLM-generated Python into a linked GH Python Script component and routes its errors. Drag the bottom grip to the target Python component.",
            "GhPython")
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
        m_attributes = new PyTransmitterAttrib(this);
    }

    /// <summary>
    /// Links this component to a GH Python Script component.
    /// Called by <see cref="PyTransmitterAttrib"/> when the user drops the wire.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the Python Script component to link.</param>
    public void LinkTo(Guid guid)
    {
        _linkedGuid = guid;
    }

    /// <summary>
    /// Removes the current link. Does not modify the previously-linked component's code.
    /// Called by <see cref="PyTransmitterAttrib"/> when the user Ctrl+drops the wire.
    /// </summary>
    public void Unlink()
    {
        _linkedGuid = Guid.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The validated PythonComponent JSON ({ code, inputs[], outputs[] }) arrives as the
    /// consumed signal's payload (Auditor's Success Signal).
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

        GhPythonBridge.SetScript(target, code);
        if (inputs.Count > 0)
            GhPythonBridge.SetInputs(target, inputs);
        if (outputs.Count > 0)
            GhPythonBridge.SetOutputs(target, outputs);

        _pendingCode = code;
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
        if (_pushError != null)
            return true;

        IGH_DocumentObject? target = ResolveTarget(out _);
        return target is null || GhPythonBridge.HasComputed(target);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the target's fresh runtime errors (after its push-triggered re-solve),
    /// filters out unconnected-input complaints, and routes Success (the code) or Fail
    /// (the error text) accordingly.
    /// </remarks>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        if (_pushError != null)
            return RoutingResult.Fail(_pushError, _pushError, GH_RuntimeMessageLevel.Error);

        IGH_DocumentObject? target = ResolveTarget(out string? linkError);
        if (target is null)
            return RoutingResult.Fail(linkError ?? "Linked Python component unavailable.", linkError, GH_RuntimeMessageLevel.Error);

        List<string> realErrors = GhPythonBridge.GetErrors(target)
            .Where(message => !IsInputConnectionError(message, target))
            .ToList();

        return realErrors.Count > 0
            ? RoutingResult.Fail(BuildFeedback(realErrors), "Target Python reported errors.", GH_RuntimeMessageLevel.Warning)
            : RoutingResult.Ok(_pendingCode);
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
            error = "No Python Script component linked. Drag from the bottom grip to connect.";
            return null;
        }

        IGH_DocumentObject? linked = OnPingDocument()?.FindObject(_linkedGuid, false);
        if (linked is null || !GhPythonBridge.IsScriptComponent(linked))
        {
            error = "Linked component not found or is not a Python Script component.";
            return null;
        }

        return linked;
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
            outputs = ParseParams(root, "outputs");

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
