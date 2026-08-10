// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;
using Physalia.GH.Components;

namespace Physalia.GH.Harness;

/// <summary>
/// A proxy node holding a Physalia pipeline in its own Grasshopper document. Right-click →
/// "Edit Harness" takes the canvas into that document — a secondary screen, the way a cluster does;
/// the canvas return widget (or File, "Save and Return") brings you back. Double-clicking opens the
/// chat window on the Chat inside, because the proxy is the only face that pipeline has on the
/// user's canvas.
///
/// <para>Unlike a cluster this needs no input or output hooks. A Chat has no inputs and one
/// output, and a Physalia pipeline never exchanges dataflow with the user's canvas: it only
/// <em>scans</em> it (grounders, guardrails) and <em>writes to it by side effect</em> (component
/// placement). So the whole pipeline, Chat included, lives inside and nothing crosses the boundary
/// as a wire.</para>
///
/// <para>The inner document is kept enabled while it is off-canvas so the pipeline keeps solving
/// while you work on your model — an async LLM response must be able to land at any time. It is
/// persisted inside this component's own archive, so a harness travels in the host .gh file.</para>
///
/// <para>Components inside the harness see the inner document from <c>OnPingDocument()</c>; anything
/// that means "the user's canvas" must go through <see cref="PhyDocuments"/>.</para>
/// </summary>
public sealed class HarnessComponent : PhyBase
{
    // Archive chunk holding the inner document, written by GH_Document.Write and rehydrated by
    // GH_Document.Read. This is the only nested-archive persistence in the plug-in; every other
    // Write/Read override in Physalia writes flat keys and guid references.
    private const string DocumentChunk = "HarnessDocument";

    // Margin (canvas units) around the harness contents when the view zooms to fit on entry.
    private const int ZoomMargin = 5;

    // Duration in ms of the animated zoom-to-fit, matching GH's own cluster-editing transition.
    private const int ZoomDuration = 250;

    // Maps a harness document back to the component holding it.
    //
    // Deliberately NOT GH_Document.Owner, even though that is what a cluster uses. Setting Owner
    // makes GH_Canvas paint its own hard-coded cluster icon at the top-left of the canvas (a
    // private _regionCluster, not a widget, so it cannot be removed from the widget list) whose
    // menu runs "Save and Return" — and that calls GH_DocumentServer.RemoveDocument, which
    // DISPOSES the document and would destroy the running pipeline. Owning the mapping ourselves
    // leaves the harness return widget as the only affordance, and as a bonus keeps the document's
    // render queue enabled (Owner's setter disables it), so previews from inside a harness survive.
    //
    // Weak on the document, so a discarded harness document is collected normally.
    private static readonly ConditionalWeakTable<GH_Document, HarnessComponent> Owners = new();

    private GH_Document? _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessComponent"/> class.
    /// </summary>
    public HarnessComponent()
        : base("Harness", "Harness", "Holds a Physalia pipeline in its own document. Double-click to open its chat window; right-click \"Edit Harness\" to edit it on a secondary canvas.", "Pipeline")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C4E1A9D2-6B33-4F58-9A07-2D5E8C1B4F60");

    /// <summary>
    /// Gets the document holding the pipeline, or null when this harness has never been opened
    /// or populated. Does not create one — use <see cref="EnsureInnerDocument"/> for that, so a
    /// mere lookup never leaves empty documents behind.
    /// </summary>
    public GH_Document? InnerDocument => _inner;

    /// <summary>
    /// Gets the number of objects held in the harness.
    /// </summary>
    public int Count => _inner?.ObjectCount ?? 0;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new Attributes.HarnessAttrib(this);
    }

    /// <summary>
    /// Gets the menu label for saving this harness to the preset library. Shared with the canvas
    /// widget inside the harness, so the same action never ends up with two names.
    /// </summary>
    internal static string SavePresetLabel => "Save Harness as Preset…";

    /// <inheritdoc/>
    /// <remarks>
    /// Editing the harness is the right-click action, because double-click is spent on the chat
    /// window — the harness node stands in for the Chat that moved inside it, so it behaves like
    /// one.
    /// </remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Edit Harness", (_, _) => OpenInCanvas());
        Menu_AppendItem(menu, SavePresetLabel, (_, _) => SaveAsPreset());
    }

    /// <summary>
    /// Saves this harness's pipeline into the user preset folder, so it can be placed again from the
    /// chat window's preset gallery.
    ///
    /// <para>Prompts for a name, confirms an overwrite, and reports the outcome on the Rhino command
    /// line. Reachable from this component's right-click menu and from the harness widget shown while
    /// you are inside the harness. Shows dialogs, so it must run on the UI thread.</para>
    /// </summary>
    public void SaveAsPreset()
    {
        if (_inner is not { ObjectCount: > 0 } contents)
        {
            Rhino.UI.Dialogs.ShowMessage(
                "This harness is empty — there is no pipeline to save.", SavePresetLabel);
            return;
        }

        // A preset the gallery cannot load is worse than none: placing one re-points the chat window
        // at the Chat inside it, so one without a Chat is refused at LOAD time. Refuse to write it
        // too, while there is still a user here to be told why.
        if (!contents.Objects.OfType<Chat>().Any())
        {
            Rhino.UI.Dialogs.ShowMessage(
                "This harness has no Chat component, so it could not be loaded back as a preset. "
                + "Add a Chat inside the harness and save again.",
                SavePresetLabel);
            return;
        }

        if (!Rhino.UI.Dialogs.ShowEditBox(SavePresetLabel, "Preset name:", NickName, false, out string typed))
        {
            return; // cancelled
        }

        if (!PresetLibrary.TryResolveUserPresetPath(typed, out string path, out string error))
        {
            Rhino.UI.Dialogs.ShowMessage(error, SavePresetLabel);
            return;
        }

        if (File.Exists(path)
            && Rhino.UI.Dialogs.ShowMessage(
                $"\"{Path.GetFileName(path)}\" already exists in your presets. Replace it?",
                SavePresetLabel,
                Rhino.UI.ShowMessageButton.YesNo,
                Rhino.UI.ShowMessageIcon.Question) != Rhino.UI.ShowMessageResult.Yes)
        {
            return;
        }

        if (!PresetLibrary.TryWrite(contents, path, out error))
        {
            Rhino.UI.Dialogs.ShowMessage($"The preset could not be saved: {error}", SavePresetLabel);
            return;
        }

        Rhino.RhinoApp.WriteLine($"[Physalia] Saved harness preset: {path}");
    }

    /// <summary>
    /// Reads a Grasshopper file into a detached document, ready to become a harness's contents.
    ///
    /// <para>Reads the archive directly rather than going through <c>GH_DocumentIO.Open</c>, which
    /// stamps the file path onto the document and appends it to Grasshopper's recent-files list —
    /// neither is wanted for bundled content the user never opened.</para>
    ///
    /// <para>The objects are given fresh instance ids on the way out. An archive carries the ids it was
    /// saved with, so without this, loading the same preset twice would put two objects with the SAME
    /// InstanceGuid in one file — which everything that identifies an object by its id then has to
    /// guess between. Grasshopper re-issues ids on paste for the same reason.</para>
    /// </summary>
    /// <param name="path">The .gh (or .ghx) file to read.</param>
    /// <returns>The loaded document, or null when the file could not be read.</returns>
    internal static GH_Document? ReadDocumentFile(string path)
    {
        var archive = new GH_Archive();
        if (!archive.ReadFromFile(path))
        {
            return null;
        }

        var document = new GH_Document();
        if (!archive.ExtractObject(document, "Definition"))
        {
            return null;
        }

        // Proxy sources first: re-issuing ids while any source is still an unresolved id reference
        // would strand it, which is why MutateAllIds documents this as a prerequisite.
        document.DestroyProxySources();
        DocumentIds.MutateAll(document);
        return document;
    }

    /// <summary>
    /// Creates a harness holding the given document. The caller places the proxy.
    /// </summary>
    /// <param name="contents">The document to hold.</param>
    /// <returns>The new harness.</returns>
    internal static HarnessComponent CreateWith(GH_Document contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var harness = new HarnessComponent();
        harness.CreateAttributes();
        harness.Adopt(contents);
        return harness;
    }

    /// <summary>
    /// Gets the harness document, creating it on first use.
    /// </summary>
    /// <returns>The inner document, owned by this component.</returns>
    public GH_Document EnsureInnerDocument()
    {
        if (_inner is null)
        {
            Adopt(new GH_Document());
        }

        return _inner!;
    }

    /// <summary>
    /// Takes the canvas into the harness document, registering it with the document server on
    /// first entry and zooming to fit its contents. This is the same sequence Grasshopper's own
    /// <c>GH_Cluster.EditClusterAsSeparateDocument</c> runs, minus the duplicate: Physalia edits
    /// the live document because conversations, signals and solve state are session-only, so a
    /// serialize/deserialize round-trip would reset the running pipeline.
    /// </summary>
    public void OpenInCanvas()
    {
        GH_Canvas? canvas = Instances.ActiveCanvas;
        if (canvas is null)
        {
            return;
        }

        GH_Document inner = EnsureInnerDocument();
        if (ReferenceEquals(canvas.Document, inner))
        {
            return; // already inside
        }

        // The document is deliberately NOT registered with the document server. The canvas setter
        // only calls PromoteDocument on it, which no-ops for an unregistered document, so pointing
        // the canvas here is enough. Staying out of the server keeps the harness off the document
        // dropdown, out of the exit-time "save changes?" sweep, and — most importantly — out of
        // reach of RemoveDocument, which disposes.
        canvas.Document = inner;
        inner.Enabled = true;

        // The canvas hit-tests against the document's attribute cache, and this document has just
        // arrived from somewhere that never had a canvas — built in memory by a placement, or read
        // out of a preset archive. Dropping the cache on entry forces it to be rebuilt against what
        // the document actually holds; without it, objects render (rendering walks Objects) but
        // cannot be selected or dragged.
        inner.DestroyAttributeCache();

        ZoomToFit(canvas, inner);
        inner.NewSolution(expireAllObjects: false);
    }

    /// <summary>
    /// Returns the canvas to the document this harness sits on, leaving the pipeline running.
    /// </summary>
    public void ReturnToHost()
    {
        GH_Canvas? canvas = Instances.ActiveCanvas;
        GH_Document? host = OnPingDocument();
        if (canvas is null || host is null)
        {
            return;
        }

        canvas.Document = host;

        // Swapping the canvas document disables the outgoing one, which would stop the pipeline
        // from solving the moment you leave it. Re-enable so an in-flight LLM response can still
        // land while you work on your model.
        ReviveInner();

        Attributes?.ExpireLayout();
        canvas.Refresh();
    }

    /// <summary>
    /// Gets the harness holding a document, or null when the document is an ordinary file.
    /// </summary>
    /// <param name="document">The document to look up.</param>
    /// <returns>The owning harness component, or null.</returns>
    internal static HarnessComponent? OwnerOf(GH_Document? document) =>
        document is not null && Owners.TryGetValue(document, out HarnessComponent? owner) ? owner : null;

    /// <summary>
    /// Finds the single transmitter inside this harness whose drag arrow the proxy should host.
    /// </summary>
    /// <param name="arrow">The sole transmitter, when there is exactly one.</param>
    /// <returns>True when exactly one transmitter is present.</returns>
    internal bool TryGetSoleArrow(out IHarnessArrow? arrow)
    {
        arrow = null;
        if (_inner is null)
        {
            return false;
        }

        List<IHarnessArrow> found = _inner.Objects.OfType<IHarnessArrow>().Take(2).ToList();
        if (found.Count != 1)
        {
            return false; // none to host, or several with no way to choose between them
        }

        arrow = found[0];
        return true;
    }

    /// <summary>
    /// Finds the Chat driving this harness's pipeline, so the proxy can open the chat window.
    /// </summary>
    /// <returns>The first Chat inside the harness, or null when it holds none.</returns>
    internal Chat? FindChat() => _inner?.Objects.OfType<Chat>().FirstOrDefault();

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        if (_inner is not null)
        {
            _inner.Write(writer.CreateChunk(DocumentChunk));

            // The harness contents are saved inside the host file, so the sub-document is no longer
            // dirty in its own right. Clearing the flag stops Grasshopper prompting to save it
            // separately when it tears subsidiary documents down on exit.
            _inner.IsModified = false;
        }

        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        GH_IReader? chunk = reader.ChunkExists(DocumentChunk) ? reader.FindChunk(DocumentChunk) : null;
        if (chunk is not null)
        {
            var doc = new GH_Document();
            if (doc.Read(chunk))
            {
                Adopt(doc);
            }
        }

        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs — the pipeline inside is self-contained and crosses no wires.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No outputs — the pipeline acts on the host canvas by side effect, never by wire.
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // The harness itself computes nothing; it keeps the inner document alive and reports what
        // it holds. Enabled is re-asserted every solve because leaving the harness on the canvas
        // clears it, and a disabled document ignores the scheduled solutions the pipeline runs on.
        ReviveInner();

        int count = Count;
        Message = count == 0 ? "empty" : $"{count} component{(count == 1 ? string.Empty : "s")}";
    }

    // Adopts a document as this harness's contents: recorded in the owner table (so PhyDocuments
    // can climb back to the user's canvas) and enabled so it keeps solving while off-canvas.
    private void Adopt(GH_Document document)
    {
        if (_inner is not null)
        {
            Owners.Remove(_inner);
        }

        Owners.Remove(document);
        Owners.Add(document, this);

        document.Enabled = true;
        _inner = document;
    }

    // Re-asserts the inner document's enabled state unless it is the one currently on the canvas,
    // where Grasshopper owns the flag.
    private void ReviveInner()
    {
        if (_inner is null || ReferenceEquals(Instances.ActiveCanvas?.Document, _inner))
        {
            return;
        }

        _inner.Enabled = true;
    }

    // Frames the harness contents in the viewport, mirroring GH's own cluster-editing transition.
    private static void ZoomToFit(GH_Canvas canvas, GH_Document document)
    {
        if (!document.Objects.Any())
        {
            return;
        }

        Rectangle port = canvas.Viewport.ScreenPort;
        Rectangle contents = GH_Convert.ToRectangle(document.BoundingBox());
        contents.Inflate(ZoomMargin, ZoomMargin);
        port.Inflate(-ZoomMargin, -ZoomMargin);

        new GH_NamedView(port, contents).SetToViewport(canvas, ZoomDuration);
    }
}
