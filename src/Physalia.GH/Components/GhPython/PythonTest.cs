// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.GH.Attributes;
using Physalia.GH.Generation;

namespace Physalia.GH.Components.GhPython;

/// <summary>
/// Test harness for the GhPythonBridge. Link to a GH Python Script component
/// via the bottom-centre bezier grip. When Update fires, pushes the Script text
/// and Input parameter names to the linked component, then reports its errors.
/// </summary>
public class PythonTest : PhyBase
{
    private Guid _linkedGuid = Guid.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PythonTest"/> class.
    /// </summary>
    public PythonTest()
        : base("Python Test", "PyTest", "Test harness for the GhPythonBridge. Drag the bottom grip to a GH Python Script component.", "GhPython")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C4A8E2F3-7B6D-4E91-A3C2-8F5D1E9B0A47");

    /// <summary>
    /// Gets the InstanceGuid of the linked GH Python Script component, or <see cref="Guid.Empty"/> if unlinked.
    /// </summary>
    public Guid LinkedGuid => _linkedGuid;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new PythonTestAttrib(this);
    }

    /// <summary>
    /// Links this component to a GH Python Script component.
    /// Called by <see cref="PythonTestAttrib"/> when the user drops the wire.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the Python Script component to link.</param>
    public void LinkTo(Guid guid)
    {
        _linkedGuid = guid;
    }

    /// <summary>
    /// Removes the current link.
    /// Called by <see cref="PythonTestAttrib"/> when the user Ctrl+drops the wire.
    /// </summary>
    public void Unlink()
    {
        _linkedGuid = Guid.Empty;
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Script", "S", "Python source code to push to the linked component.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Inputs", "I", "Input parameter names to set on the linked component.", GH_ParamAccess.list);
        pManager.AddTextParameter("Outputs", "O", "Output parameter names to set on the linked component.", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Update", "U", "Rising edge pushes Script, Inputs, and Outputs to the linked component.", GH_ParamAccess.item, false);
        pManager[1].Optional = true;
        pManager[2].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Errors", "E", "Runtime error messages from the linked Python Script component.", GH_ParamAccess.list);
        pManager.AddTextParameter("Input Values", "IV", "Current input parameter values on the linked component as 'name: value' strings.", GH_ParamAccess.list);
        pManager.AddTextParameter("Output Values", "OV", "Current output parameter values on the linked component as 'name: value' strings.", GH_ParamAccess.list);
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

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string script = string.Empty;
        var inputs = new List<string>();
        var outputs = new List<string>();
        bool update = false;

        DA.GetData(0, ref script);
        DA.GetDataList(1, inputs);
        DA.GetDataList(2, outputs);
        DA.GetData(3, ref update);

        if (_linkedGuid == Guid.Empty)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Python Script component linked. Drag from the bottom grip to connect.");
            DA.SetDataList(0, Array.Empty<string>());
            DA.SetDataList(1, Array.Empty<string>());
            DA.SetDataList(2, Array.Empty<string>());
            return;
        }

        var doc = OnPingDocument();
        var linked = doc?.FindObject(_linkedGuid, false);

        if (linked == null || !GhPythonBridge.IsScriptComponent(linked))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Linked component not found or is not a Python Script component.");
            DA.SetDataList(0, Array.Empty<string>());
            DA.SetDataList(1, Array.Empty<string>());
            DA.SetDataList(2, Array.Empty<string>());
            return;
        }

        if (update)
        {
            if (!string.IsNullOrEmpty(script))
                GhPythonBridge.SetScript(linked, script);

            if (inputs.Count > 0)
                GhPythonBridge.SetInputs(linked, inputs);

            if (outputs.Count > 0)
                GhPythonBridge.SetOutputs(linked, outputs);

            GhPythonBridge.Expire(linked);
        }

        DA.SetDataList(0, GhPythonBridge.GetErrors(linked));
        DA.SetDataList(1, GhPythonBridge.GetInputValues(linked));
        DA.SetDataList(2, GhPythonBridge.GetOutputValues(linked));
    }
}
