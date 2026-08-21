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
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;
using Physalia.GH.Components;
using Physalia.GH.Parameters;

namespace Physalia.GH.Harness;

/// <summary>
/// A proxy node holding a Physalia pipeline in its own Grasshopper document. Right-click →
/// "Edit Harness" takes the canvas into that document — a secondary screen, the way a cluster does;
/// the canvas return widget (or File, "Save and Return") brings you back. Double-clicking opens the
/// chat window on the Chat inside, because the proxy is the only face that pipeline has on the
/// user's canvas.
///
/// <para>Nothing crosses the boundary OUTWARD as a wire. What a pipeline produces is an edit to the
/// user's canvas — component placement, a script pushed into a node — so its outputs are side effects
/// carried by the drag arrows the proxy hosts (see <see cref="IHarnessOutlet"/>), never dataflow.</para>
///
/// <para>Inward is the other way round, and the asymmetry is the point. What a pipeline consumes is
/// data the canvas already computed, and Grasshopper hands us wires pointing inward for free — so a
/// <c>Harness In</c> placed inside grows an ordinary input parameter on the LEFT edge of this proxy
/// (see <see cref="IHarnessInlet"/>), with ordinary expiry and ordinary solve ordering. The proxy's
/// solve hands each inlet's tree to its node and schedules ONE solution on the harness document;
/// arriving data never mints a signal, so it can start no round and can close no loop.</para>
///
/// <para>The inner document is kept enabled while it is off-canvas so the pipeline keeps solving
/// while you work on your model — an async LLM response must be able to land at any time. It is
/// persisted inside this component's own archive, so a harness travels in the host .gh file.</para>
///
/// <para>Components inside the harness see the inner document from <c>OnPingDocument()</c>; anything
/// that means "the user's canvas" must go through <see cref="PhyDocuments"/>.</para>
/// </summary>
public sealed class HarnessComponent : PhyBase, IGH_VariableParameterComponent
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

    private bool _syncHooked;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessComponent"/> class.
    /// </summary>
    public HarnessComponent()
        : base("Harness", "Harness", "Holds a whole Physalia pipeline inside its own document, so your canvas carries one node instead of thirty. Double-click to open its chat window; right-click and choose Edit Harness to work on the pipeline inside.", "Pipeline")
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

    /// <summary>
    /// Gets the menu label for loading a harness pipeline in from a file. Shared with the canvas
    /// widget inside the harness, for the same reason the save label is.
    /// </summary>
    internal static string LoadFileLabel => "Load Harness from .gh File…";

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
        Menu_AppendItem(menu, LoadFileLabel, (_, _) => LoadFromFile());
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
    /// Replaces this harness's pipeline with one read from a Grasshopper file — the reverse of
    /// <see cref="SaveAsPreset"/>, and the way a harness someone sent you (or one saved outside the
    /// preset library) gets in.
    ///
    /// <para>A harness file is an ordinary <c>.gh</c> holding one harness's worth of pipeline, so
    /// anything the preset gallery can place can be loaded here — and anything saved from here can be
    /// loaded there. The file is read exactly as a preset is (fresh instance ids, host targets
    /// cleared), so loading the same file into two harnesses is safe.</para>
    ///
    /// <para>This DISCARDS what is in the harness now: its conversation, its solve state and any warm
    /// provider session go with it, and none of it is on the undo stack — hence the confirmation. A
    /// file carrying no Chat is refused before anything is touched, because the pipeline would have
    /// nothing to drive it and the chat window nothing to land on.</para>
    ///
    /// <para>Shows dialogs, so it must run on the UI thread. Reachable from this component's
    /// right-click menu and from the harness widget shown while you are inside the harness.</para>
    /// </summary>
    public void LoadFromFile()
    {
        if (PromptForHarnessFile() is not { } path)
        {
            return; // cancelled
        }

        GH_Document? contents;
        try
        {
            contents = ReadDocumentFile(path);
        }
        catch (Exception ex)
        {
            Rhino.UI.Dialogs.ShowMessage($"The file could not be read: {ex.Message}", LoadFileLabel);
            return;
        }

        if (contents is null)
        {
            Rhino.UI.Dialogs.ShowMessage(
                $"\"{Path.GetFileName(path)}\" is not a Grasshopper definition, or could not be read.",
                LoadFileLabel);
            return;
        }

        // Same rule as the preset gallery applies, for the same reason: a harness with no Chat has
        // nothing driving it, and the chat window would be left with nothing to view.
        if (!contents.Objects.OfType<Chat>().Any())
        {
            Rhino.UI.Dialogs.ShowMessage(
                $"\"{Path.GetFileName(path)}\" contains no Chat component, so it is not a harness "
                + "pipeline. Save one from inside a harness that has a Chat, then load that.",
                LoadFileLabel);
            return;
        }

        // Only an empty harness is replaced without asking — there is nothing there to lose.
        if (_inner is { ObjectCount: > 0 }
            && Rhino.UI.Dialogs.ShowMessage(
                $"Loading \"{Path.GetFileName(path)}\" replaces the pipeline in this harness, "
                + "including its conversation. Its Harness In nodes go with it, so any wires feeding this "
                + "harness's inputs are dropped. This cannot be undone. Continue?",
                LoadFileLabel,
                Rhino.UI.ShowMessageButton.YesNo,
                Rhino.UI.ShowMessageIcon.Warning) != Rhino.UI.ShowMessageResult.Yes)
        {
            return;
        }

        Replace(contents);
        Rhino.RhinoApp.WriteLine($"[Physalia] Loaded harness pipeline from: {path}");
    }

    /// <summary>
    /// Asks for the Grasshopper file to load, starting in the folder the host file lives in.
    /// </summary>
    /// <returns>The chosen path, or null when the user cancelled.</returns>
    private string? PromptForHarnessFile()
    {
        var dialog = new Rhino.UI.OpenFileDialog
        {
            Title = LoadFileLabel,
            Filter = "Grasshopper files (*.gh;*.ghx)|*.gh;*.ghx|All files (*.*)|*.*",
            DefaultExt = "gh",
        };

        // The host file's folder is the likeliest place a harness sent to you was saved next to.
        // Anything else (an unsaved document) leaves the dialog on its own last-used folder.
        string? hostFile = OnPingDocument()?.FilePath;
        if (!string.IsNullOrEmpty(hostFile) && Path.GetDirectoryName(hostFile) is { Length: > 0 } dir)
        {
            dialog.InitialDirectory = dir;
        }

        return dialog.ShowOpenDialog() ? dialog.FileName : null;
    }

    /// <summary>
    /// Swaps in a new set of contents and tears the old ones down.
    ///
    /// <para>Order is load-bearing. The new document is adopted FIRST, so anything that reacts to the
    /// old pipeline being dismantled — the chat window, above all — already sees this harness holding
    /// the replacement. If the canvas is standing inside the harness it is re-pointed before the old
    /// document is touched, because emptying the document being drawn is not something to ask
    /// Grasshopper to survive.</para>
    /// </summary>
    /// <param name="contents">The freshly read document to hold.</param>
    private void Replace(GH_Document contents)
    {
        GH_Document? previous = _inner;
        GH_Canvas? canvas = Instances.ActiveCanvas;
        bool inside = previous is not null && ReferenceEquals(canvas?.Document, previous);

        // Captured BEFORE the teardown: removing the old Chats makes the window re-point itself, and
        // it settles on the first Chat it finds anywhere in the file — which may belong to another
        // harness entirely. Knowing it was watching THIS harness is what lets it be brought back.
        bool windowWasHere = Chat.ActiveWindow is { } window && Chats.Any(window.IsViewing);

        Adopt(contents);

        if (inside)
        {
            OpenInCanvas();
        }

        Retire(previous);

        // The loaded Chats carry the emoji they were saved with, so they can collide with one already
        // in the file. They could not see their siblings until the harness holding them was adopted.
        foreach (Chat chat in Chats)
        {
            chat.EnsureDistinctEmoji();
        }

        ExpireProxyLayout();
        ExpireSolution(true);

        if (windowWasHere && FindChat() is { } loaded)
        {
            Chat.ActiveWindow?.SetActiveComponent(loaded);
        }

        canvas?.Refresh();
    }

    /// <summary>
    /// Shuts a discarded harness document down.
    ///
    /// <para>Dropping the reference is not enough: a pipeline holds things the garbage collector
    /// knows nothing about — a warm CLI session on an LLM Call, host-document subscriptions on a
    /// Script I/O, an idle poll on a group follower. Every one of those is released from
    /// <c>RemovedFromDocument</c>, which is why the objects are removed rather than abandoned.</para>
    /// </summary>
    /// <param name="previous">The document being discarded, or null when the harness was empty.</param>
    private static void Retire(GH_Document? previous)
    {
        if (previous is null)
        {
            return;
        }

        try
        {
            previous.Enabled = false;

            List<IGH_DocumentObject> objects = previous.Objects.ToList();
            if (objects.Count > 0)
            {
                previous.RemoveObjects(objects, false);
            }

            previous.Dispose();
        }
        catch (Exception ex)
        {
            // A pipeline that will not shut down cleanly is not a reason to leave the harness holding
            // two documents; the new one is already in place, so say so and carry on.
            Rhino.RhinoApp.WriteLine(
                $"[Physalia] The replaced harness pipeline did not shut down cleanly: {ex.Message}");
        }
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
        ClearHostTargets(document);
        return document;
    }

    /// <summary>
    /// Forgets what every outlet in a just-loaded preset was aiming at on its author's canvas.
    ///
    /// <para>Ids and pivots are the same problem seen twice. <see cref="DocumentIds.MutateAll"/>
    /// handles the half that lives INSIDE the harness, where a duplicate id is the hazard; this is the
    /// half that points OUT of it, where the hazard is a target that means nothing here. A linked
    /// script component is at least self-cancelling — the id resolves to nothing on this canvas, so
    /// the wire simply never draws — but a Component Transmitter's placement point is stored as an
    /// offset from the proxy's own pivot, so it resolves perfectly well against the wrong document and
    /// the preset lands with a wire already stretched out to wherever its author dropped one.
    /// (<c>Files/PRESETS/Physalia/Codex - Incremental Nodes.gh</c> ships carrying exactly that.)</para>
    ///
    /// <para>Only outlets are swept. Links BETWEEN components in the harness — a Feedback Collector's,
    /// a Script I/O's — are part of the pipeline being loaded and are re-pointed by
    /// <see cref="IGuidLinked.RemapLinks"/>, not dropped.</para>
    /// </summary>
    /// <param name="document">The freshly loaded preset document.</param>
    private static void ClearHostTargets(GH_Document document)
    {
        foreach (IHarnessOutlet outlet in document.Objects.OfType<IHarnessOutlet>())
        {
            outlet.ClearHostTarget();
        }

        // The inward half of the same sweep. A Harness In's held data is session-only and so cannot
        // arrive in an archive, but a document read here may also be one being re-adopted, and data
        // from the canvas it used to be fed by means nothing on this one.
        foreach (IHarnessInlet inlet in document.Objects.OfType<IHarnessInlet>())
        {
            inlet.ClearInlet();
        }
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
    /// Gets the transmitters inside this harness, in the order the proxy stacks their grips down its
    /// right edge — one grip per transmitter, since a harness with two of them (say a node and a
    /// Python transmitter) has two distinct reaches onto the user's canvas.
    ///
    /// <para>Ordered by where the transmitters sit INSIDE the harness, top to bottom then left to
    /// right, so the grips read in the same order as the pipeline they belong to and stay put across
    /// sessions without anything being serialized. Moving a transmitter inside re-orders the grips,
    /// which is the only way a user can control that order — and the only ordering that survives a
    /// document being re-read, since instance ids are re-issued when a preset is loaded.</para>
    /// </summary>
    internal IReadOnlyList<IHarnessOutlet> Outlets =>
        _inner is null
            ? Array.Empty<IHarnessOutlet>()
            : _inner.Objects
                .OfType<IHarnessOutlet>()
                .OrderBy(outlet => ((IGH_DocumentObject)outlet).Attributes.Pivot.Y)
                .ThenBy(outlet => ((IGH_DocumentObject)outlet).Attributes.Pivot.X)
                .ToList();

    /// <summary>
    /// Gets the Harness In nodes inside this harness, in the order the proxy stacks their inputs down its
    /// left edge — one input per node, since a harness with two of them takes two distinct feeds
    /// off the user's canvas.
    ///
    /// <para>Ordered the same way <see cref="Outlets"/> is, and for the same reason: by where they
    /// sit INSIDE the harness, top to bottom then left to right, so the inputs read in the
    /// order of the pipeline they belong to and stay put across sessions without anything being
    /// serialized. Moving a Harness In inside re-orders the inputs — and because each input is BOUND to
    /// its node by InstanceGuid rather than by position, the wires move with them instead of
    /// silently swapping one node's data for another's.</para>
    /// </summary>
    internal IReadOnlyList<IHarnessInlet> Inlets =>
        _inner is null
            ? Array.Empty<IHarnessInlet>()
            : _inner.Objects
                .OfType<IHarnessInlet>()
                .OrderBy(inlet => ((IGH_DocumentObject)inlet).Attributes?.Pivot.Y ?? 0f)
                .ThenBy(inlet => ((IGH_DocumentObject)inlet).Attributes?.Pivot.X ?? 0f)
                .ToList();

    /// <summary>
    /// Gets the Chats inside this harness, in the order the chat window's switcher row shows them —
    /// by pivot, left to right then top to bottom. The proxy wears their emoji as its icon, so the
    /// node on the canvas and the row of circles in the window read as the same list; matching
    /// <c>ChatWindow.CompareChats</c> is what keeps them in step.
    /// </summary>
    internal IReadOnlyList<Chat> Chats =>
        _inner is null
            ? Array.Empty<Chat>()
            : _inner.Objects
                .OfType<Chat>()
                .OrderBy(chat => chat.Attributes?.Pivot.X ?? 0f)
                .ThenBy(chat => chat.Attributes?.Pivot.Y ?? 0f)
                .ToList();

    /// <summary>
    /// Finds the Chat driving this harness's pipeline, so the proxy can open the chat window.
    /// </summary>
    /// <returns>The first Chat inside the harness, or null when it holds none.</returns>
    internal Chat? FindChat() => Chats.FirstOrDefault();

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
    /// <remarks>
    /// None to start with. The proxy's inputs are DERIVED from the Harness In nodes inside the harness and
    /// are grown by <see cref="SyncInlets"/>, so a harness holding no Harness In has no inputs at all.
    /// </remarks>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        // No outputs — the pipeline acts on the host canvas by side effect, never by wire.
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Never: the input set is derived from the harness contents, so the zoomable +/- icons would
    /// offer the user a parameter belonging to no node. Implementing the interface at all is what
    /// tells Grasshopper this component's parameters are not the ones <c>RegisterInputParams</c>
    /// registered, so an archived set is restored rather than discarded.
    /// </remarks>
    public bool CanInsertParameter(GH_ParameterSide side, int index) => false;

    /// <inheritdoc/>
    /// <remarks>Never, for the same reason — remove the Harness In inside instead.</remarks>
    public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;

    /// <inheritdoc/>
    public IGH_Param CreateParameter(GH_ParameterSide side, int index) => new Param_Inlet();

    /// <inheritdoc/>
    public bool DestroyParameter(GH_ParameterSide side, int index) => true;

    /// <inheritdoc/>
    /// <remarks>Nothing to maintain — <see cref="SyncInlets"/> owns the whole input set.</remarks>
    public void VariableParameterMaintenance()
    {
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // The harness itself computes nothing. Enabled is re-asserted every solve because leaving the
        // harness on the canvas clears it, and a disabled document ignores the scheduled solutions the
        // pipeline runs on.
        //
        // Deliberately sets no Message: the component count used to ride under the node as GH's black
        // message tag, which said little that mattered and cluttered the canvas.
        ReviveInner();

        PushInlets(DA);

        // Moving a Harness In inside the harness re-orders the inputs out here, and NOTHING announces a
        // move — no event, and not reliably a layout either (see RefreshInlets). The solve is the one
        // hook that runs often and runs for certain, so the drift is checked here and handed to the
        // idle sync, which is where the parameter list may actually be rebuilt.
        if (InletsDiffer())
        {
            RequestInletSync();
        }
    }

    /// <inheritdoc/>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        // Covers the load path: Read has rehydrated both the harness contents and this proxy's
        // archived inputs by now, so the sync's job here is to MATCH them up — and to build the set
        // from scratch for a harness that arrived some other way, such as a preset placement.
        RequestInletSync();
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        UnhookSync();
        base.RemovedFromDocument(document);
    }

    // Hands each inlet the data on its parameter and, when any of it is new, schedules ONE solution on
    // the harness document with those Harness In nodes expired.
    //
    // Deferred rather than solved here: this is the HOST's solution, and the harness is a different
    // document with its own solver. Expiring inside a scheduled callback is the safe shape — GH flushes
    // scheduled delegates at the start of the solution it is about to run, so marking expired is
    // exactly enough, and a sub-document solution cannot re-enter the one running now.
    private void PushInlets(IGH_DataAccess DA)
    {
        if (_inner is null)
        {
            return;
        }

        List<IGH_ActiveObject>? touched = null;

        for (int i = 0; i < Params.Input.Count; i++)
        {
            if (Params.Input[i] is not Param_Inlet inlet)
            {
                continue;
            }

            if (FindInlet(inlet.InletId) is not { } receiver)
            {
                continue;
            }

            // An unwired input still pushes: the empty tree is what clears a Harness In that used to be
            // fed, and without it the harness would keep serving data the canvas no longer supplies.
            if (!DA.GetDataTree(i, out GH_Structure<IGH_Goo> tree) || tree is null)
            {
                tree = new GH_Structure<IGH_Goo>();
            }

            if (receiver.Accept(tree) && receiver is IGH_ActiveObject active)
            {
                (touched ??= new List<IGH_ActiveObject>()).Add(active);
            }
        }

        if (touched is null)
        {
            return;
        }

        List<IGH_ActiveObject> expire = touched;
        _inner.ScheduleSolution(1, _ =>
        {
            foreach (IGH_ActiveObject receiver in expire)
            {
                receiver.ExpireSolution(false);
            }
        });
    }

    /// <summary>
    /// Re-measures and repaints after a Harness Out's input was renamed, since its grip on this proxy
    /// is labelled with that name.
    ///
    /// <para>The label itself needs no pushing — <c>DrawOutletLabels</c> reads it live off the outlet
    /// every frame. What does need doing is the LAYOUT: the strip reserved along the right edge is
    /// measured from the labels, and a longer name wants more of it.</para>
    /// </summary>
    internal void OnOutletRenamed() => ExpireProxyLayout();

    /// <summary>
    /// Relabels the input belonging to a Harness In that has just been renamed, and repaints.
    ///
    /// <para>Called from the Harness In's output parameter, whose <c>NickName</c> setter is overridden
    /// because that is the only hook that cannot be missed: nothing raises an event for a rename, and
    /// an expired layout is not a promise that <c>Layout</c> will run. See <see cref="RefreshInlets"/>
    /// for the evidence.</para>
    /// </summary>
    /// <param name="inlet">The Harness In whose name changed.</param>
    internal void OnInletRenamed(IHarnessInlet inlet)
    {
        Guid id = ((IGH_DocumentObject)inlet).InstanceGuid;

        foreach (IGH_Param param in Params.Input)
        {
            if (param is Param_Inlet bound && bound.InletId == id)
            {
                Describe(bound, inlet);
                ExpireProxyLayout();
                return;
            }
        }
    }

    // Carries a rename made OUT here back inside, onto the Harness In's own output parameter, so the
    // two ends keep one name between them whichever is edited. The node pushes the same name back out,
    // where the parameter's equality guard stops the bounce.
    private void RenameInlet(Guid receiverId, string name)
    {
        if (FindInlet(receiverId) is { } receiver)
        {
            receiver.InletName = name;
        }
    }

    // Points a parameter at the Harness In it must rename. Re-applied on every pass rather than only on
    // creation, since a parameter restored from an archive is built by Grasshopper and arrives blank.
    private void Bind(Param_Inlet param)
    {
        param.Renamed = name => RenameInlet(param.InletId, name);
    }

    // The Harness In an input parameter belongs to, or null when it has been deleted inside the harness
    // (the next sync drops the parameter).
    private IHarnessInlet? FindInlet(Guid receiverId)
    {
        if (_inner is null || receiverId == Guid.Empty)
        {
            return null;
        }

        foreach (IHarnessInlet inlet in _inner.Objects.OfType<IHarnessInlet>())
        {
            if (((IGH_DocumentObject)inlet).InstanceGuid == receiverId)
            {
                return inlet;
            }
        }

        return null;
    }

    // Asks for a sync at the next idle. Deferred because it mutates this component's parameter set,
    // which must not happen while a solution — this document's or the harness's — is running, and
    // because the events that trigger it (objects added inside, a node renamed or moved) arrive in
    // bursts that coalesce into one pass here.
    private void RequestInletSync()
    {
        if (_syncHooked)
        {
            return;
        }

        _syncHooked = true;
        Rhino.RhinoApp.Idle += OnIdleSync;
    }

    private void UnhookSync()
    {
        if (_syncHooked)
        {
            Rhino.RhinoApp.Idle -= OnIdleSync;
            _syncHooked = false;
        }
    }

    private void OnIdleSync(object? sender, EventArgs e)
    {
        UnhookSync();

        if (!SyncInlets())
        {
            return;
        }

        Params.OnParametersChanged();
        ExpireProxyLayout();

        // Only re-solve once there is something to re-solve. A sync can run before this proxy has
        // reached a document at all: Adopt asks for one while the harness contents are still being
        // read out of an archive.
        if (OnPingDocument() is not null)
        {
            ExpireSolution(true);
        }
    }

    /// <summary>
    /// Reconciles this proxy's inputs with the Harness In nodes inside the harness: one input each,
    /// in their layout order, named after them.
    ///
    /// <para>Bound by InstanceGuid, never by position. An input parameter is a real object that other
    /// components' wires point AT — unlike an outlet's grip, which is an arrow we draw ourselves — so
    /// a parameter whose node still exists is REUSED, never rebuilt: rebuilding it would drop the
    /// wire feeding it, and re-binding by index would hand one node's data to another as soon as
    /// the nodes inside were re-ordered. Re-ordering moves the parameter objects themselves, so their
    /// sources travel with them.</para>
    /// </summary>
    /// <returns>True when anything changed, so the caller re-lays out and re-solves.</returns>
    private bool SyncInlets()
    {
        IReadOnlyList<IHarnessInlet> inlets = Inlets;
        var desired = new List<IGH_Param>(inlets.Count);
        bool created = false;
        bool relabelled = false;

        foreach (IHarnessInlet inlet in inlets)
        {
            Guid id = ((IGH_DocumentObject)inlet).InstanceGuid;

            Param_Inlet? param = Params.Input
                .OfType<Param_Inlet>()
                .FirstOrDefault(candidate => candidate.InletId == id && !desired.Contains(candidate));

            if (param is null)
            {
                param = new Param_Inlet { InletId = id };
                created = true;
            }

            Bind(param);
            relabelled |= Describe(param, inlet);
            desired.Add(param);
        }

        bool rebuild = created || Params.Input.Count != desired.Count;
        for (int i = 0; !rebuild && i < desired.Count; i++)
        {
            rebuild = !ReferenceEquals(Params.Input[i], desired[i]);
        }

        if (rebuild)
        {
            // Isolated only when the parameter is going for good; one that is merely MOVING keeps its
            // sources, which is what makes re-ordering the Harness In nodes inside harmless out here.
            foreach (IGH_Param existing in Params.Input.ToList())
            {
                Params.UnregisterInputParameter(existing, isolate: !desired.Contains(existing));
            }

            foreach (IGH_Param param in desired)
            {
                Params.RegisterInputParam(param);
            }
        }

        return rebuild || relabelled;
    }

    /// <summary>
    /// Brings the inputs back in step with the Harness In nodes inside, called from the proxy's layout pass.
    ///
    /// <para>Nothing announces either change. Grasshopper's <c>NickName</c> setter raises NOTHING
    /// (verified against the shipped assembly: the setter body is a bare field assignment) — only the
    /// right-click name box does, through <c>Menu_NameItemTextChanged</c> and friends, so an F2 or
    /// properties-panel rename reaches no handler at all. A MOVE raises nothing anywhere either, which
    /// is why <c>MasterGroupFollower</c> ended up polling.</para>
    ///
    /// <para>A rename does not rely on this, and must not: <c>PerformLayout</c> is called from a bare
    /// handful of places in Grasshopper and the paint loop is not one of them, so an expired layout can
    /// go unperformed indefinitely. Both ends of the name are kept in step by overriding the virtual
    /// <c>NickName</c> setter instead — see <see cref="Param_LinkedName"/>, which both the Harness In's
    /// output and this proxy's input derive from. What is left for this pass is repair: rebinding
    /// parameters restored from an archive, and handing a set or order change to the idle sync, since
    /// the parameter list must not be rebuilt from inside a layout pass.</para>
    /// </summary>
    internal void RefreshInlets()
    {
        foreach (IGH_Param param in Params.Input)
        {
            if (param is Param_Inlet inlet && FindInlet(inlet.InletId) is { } receiver)
            {
                Bind(inlet);
                Describe(inlet, receiver);
            }
        }

        if (InletsDiffer())
        {
            RequestInletSync();
        }
    }

    // Whether the Harness In nodes inside no longer line up with the inputs out here — one added, one
    // deleted, or the set re-ordered by moving a node.
    private bool InletsDiffer()
    {
        IReadOnlyList<IHarnessInlet> inlets = Inlets;
        if (Params.Input.Count != inlets.Count)
        {
            return true;
        }

        for (int i = 0; i < inlets.Count; i++)
        {
            if (Params.Input[i] is not Param_Inlet param
                || param.InletId != ((IGH_DocumentObject)inlets[i]).InstanceGuid)
            {
                return true;
            }
        }

        return false;
    }

    // Names an input after its Harness In. Returns whether anything actually changed, so a sync that
    // finds nothing new costs a relayout and nothing more.
    private static bool Describe(Param_Inlet param, IHarnessInlet inlet)
    {
        string name = inlet.InletName;
        string description = inlet.InletDescription;

        if (param.Name == name && param.NickName == name && param.Description == description)
        {
            return false;
        }

        param.Name = name;
        param.NickName = name;
        param.Description = description;
        return true;
    }


    // Adopts a document as this harness's contents: recorded in the owner table (so PhyDocuments
    // can climb back to the user's canvas) and enabled so it keeps solving while off-canvas.
    private void Adopt(GH_Document document)
    {
        if (_inner is not null)
        {
            Owners.Remove(_inner);

            // Named handlers rather than lambdas so a REPLACED document can be let go of properly:
            // its objects are removed on the way out, and a document still wired to this proxy would
            // expire the layout on the way past.
            _inner.ObjectsAdded -= OnInnerObjectsChanged;
            _inner.ObjectsDeleted -= OnInnerObjectsChanged;
        }

        Owners.Remove(document);
        Owners.Add(document, this);

        // The proxy grows one grip per transmitter INSIDE this document (see Outlets), so its layout
        // depends on contents Grasshopper has no idea are connected to it. Watch the object list, or a
        // transmitter added in here gets its grip only after some unrelated relayout happens to run.
        document.ObjectsAdded += OnInnerObjectsChanged;
        document.ObjectsDeleted += OnInnerObjectsChanged;

        document.Enabled = true;
        _inner = document;

        // The inputs are derived from what is in here, and adopting raises no ObjectsAdded — the
        // objects were already in the document when it arrived.
        RequestInletSync();
    }

    // Re-lays the proxy out and repaints when the harness contents change underneath it, and
    // reconciles the inputs: a Harness In added or deleted in there is an input appearing or vanishing
    // out here.
    private void OnInnerObjectsChanged(object sender, GH_DocObjectEventArgs e)
    {
        ExpireProxyLayout();
        RequestInletSync();
    }

    // Re-lays the proxy out and repaints, after the harness contents changed underneath it.
    private void ExpireProxyLayout()
    {
        Attributes?.ExpireLayout();
        Instances.RedrawCanvas();
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
