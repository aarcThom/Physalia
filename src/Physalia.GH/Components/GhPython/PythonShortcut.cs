// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using RhinoCodePlatform.GH;

namespace Physalia.GH.Components.GhPython;

/// <summary>
/// Convenience shortcut that places a stock GH Python Script component on the canvas.
/// When added to a document this component immediately replaces itself with the first
/// registered <see cref="IScriptComponent"/> proxy found in the component server.
/// </summary>
public class PythonShortcut : PhyBase
{
    private static Guid _cachedProxyGuid = Guid.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PythonShortcut"/> class.
    /// </summary>
    public PythonShortcut()
        : base("Python Script", "Py", "Places a GH Python Script component on the canvas.", "GhPython")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D229D407-DD63-401A-BF79-5E69D6022E0C");

    /// <inheritdoc/>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        var pivot = Attributes.Pivot;
        var python = CreatePythonComponent();

        if (python == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "GH Python Script component not found in the component server.");
            return;
        }

        python.Attributes.Pivot = pivot;
        document.AddObject(python, false);
        document.RemoveObject(this, false);
        python.ExpireSolution(true);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager) { }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA) { }

    // -------------------------------------------------------------------------

    private static IGH_DocumentObject? CreatePythonComponent()
    {
        // Fast path: proxy GUID already cached from a previous placement.
        if (_cachedProxyGuid != Guid.Empty)
        {
            foreach (var p in Grasshopper.Instances.ComponentServer.ObjectProxies)
            {
                if (p.Guid == _cachedProxyGuid) return p.CreateInstance();
            }
        }

        // Slow path: walk proxies once, find the first whose CLR type implements IScriptComponent.
        foreach (var proxy in Grasshopper.Instances.ComponentServer.ObjectProxies)
        {
            if (proxy.Type != null && typeof(IScriptComponent).IsAssignableFrom(proxy.Type))
            {
                _cachedProxyGuid = proxy.Guid;
                return proxy.CreateInstance();
            }
        }

        return null;
    }
}
