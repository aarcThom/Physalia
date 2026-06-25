// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Attributes;
using Physalia.GH.Panels;
using Physalia.GH.Parameters;
using HarnessGroup = Physalia.GH.Harness.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Standalone-window chat entry point. Like Prompter it is a signal source with one
/// Prompt Signal output and no conversation state of its own, but instead of a canvas
/// panel it drives a separate Eto WebView window hosting a web chat UI. Each send from
/// the window mints one Prompt Signal whose payload is the prompt text — wire it to
/// Recorder's Prompt Signal input. The classic Prompter remains available alongside it.
/// </summary>
public class Chatbox : StatefulComponentBase
{
    // Single-glyph sea/ocean emojis used as a Chatbox's visual identity — shown in place of
    // its canvas icon and as its circle in the chat window's switcher row, so the user can
    // tell which Chatbox a given dot belongs to. Kept to plain single-codepoint glyphs (no
    // ZWJ sequences or variation selectors) so TextRenderer paints them predictably.
    private static readonly string[] OceanEmoji =
    {
        "🌊", "🐠", "🐟", "🐡", "🦈", "🐙", "🐚", "🦀", "🦞", "🦐",
        "🦑", "🐳", "🐋", "🐬", "🦭", "🐢", "🪼", "🐧", "🦦", "⚓", "🪸",
    };

    // Only one chat window may exist per Rhino session, across every Chatbox instance.
    // Static so a second Chatbox switches the single window to its own view rather than
    // spawning another. Session-only — nothing here serializes.
    private static ChatWindow? _activeWindow;

    // The collapsible group of pipeline components this Chatbox proxies. All group/collapse
    // logic lives in HarnessGroup; the Chatbox just delegates.
    private readonly HarnessGroup _group;

    // Set after a Read so the collapsed state is re-applied to members once the whole
    // document has finished loading (deferred to the next solve / idle pass).
    private bool _pendingApply;

    // This Chatbox's assigned ocean emoji (its identity). Always non-empty — seeded randomly
    // in the constructor, deduped against canvas siblings on first placement, and persisted.
    private string _emoji;

    // True once this instance was restored from a file (Read ran), so first-placement dedupe
    // is skipped and a persisted emoji is never reshuffled.
    private bool _loaded;

    // The per-instance colour emoji icon (a bundled Noto bitmap scaled to 24x24); lazily built,
    // dropped when the emoji changes so the next Icon get rebuilds it.
    private Bitmap? _iconBitmap;

    /// <summary>
    /// Initializes a new instance of the <see cref="Chatbox"/> class.
    /// </summary>
    public Chatbox()
        : base("Chatbox", "Chat", "Standalone chat window driving the pipeline. Double-click to open the window; send a message to mint a Prompt Signal.", "Core")
    {
        _group = new HarnessGroup(this);
        _emoji = OceanEmoji[Random.Shared.Next(OceanEmoji.Length)];
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B7E4B6F2-3C2A-4D71-9E0A-7F1C2D3E4A5B");

    /// <summary>
    /// Gets the ocean emoji that identifies this Chatbox on the canvas (as its icon) and in
    /// the chat window's switcher row. Stable for the life of the component and persisted.
    /// </summary>
    public string Emoji => _emoji;

    /// <summary>
    /// Gets the collapsible harness group this Chatbox represents. The proxy renders a
    /// distinct collapsed capsule while the group is collapsed; the chat window and canvas
    /// menu drive it through here.
    /// </summary>
    public HarnessGroup Group => _group;

    /// <inheritdoc/>
    protected override string ClearMenuText => "Clear Signal";

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new ChatboxAttrib(this);

        // Give the output its harness-aware param attributes so the proxy drops its grips while
        // collapsed (no wires can be pulled), staying draggable. GH only auto-creates linked
        // param attributes when none are set, so pre-assigning these wins.
        foreach (IGH_Param output in Params.Output)
        {
            output.Attributes = new HarnessParamAttributes(output, m_attributes, _group);
        }
    }

    /// <summary>
    /// Gets the component icon — this Chatbox's assigned ocean emoji as a bundled colour bitmap
    /// (Noto Emoji), so each Chatbox reads as a distinct node. GDI cannot colour-render an emoji
    /// font, so a pre-made image is used rather than drawn glyphs. The ribbon/palette proxy (no
    /// document) shows the palette's first emoji as a stable, recognisable button.
    /// </summary>
    protected override Bitmap Icon =>
        _iconBitmap ??= BuildEmojiIcon(OnPingDocument() is null ? OceanEmoji[0] : _emoji);

    // Loads the bundled colour PNG for an emoji (Resources/emoji/emoji_u<codepoint>.png) and scales
    // it to a 24x24 icon. Falls back to the shared brain icon if the resource is missing.
    private Bitmap BuildEmojiIcon(string emoji)
    {
        string resource = string.IsNullOrEmpty(emoji)
            ? string.Empty
            : $"Physalia.GH.Resources.emoji.emoji_u{char.ConvertToUtf32(emoji, 0):x}.png";

        using System.IO.Stream? stream = string.IsNullOrEmpty(resource) ? null : GHAssembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            using System.IO.Stream? brain = GHAssembly.GetManifestResourceStream("Physalia.GH.Resources.brain.png");
            return brain != null ? new Bitmap(brain) : new Bitmap(24, 24);
        }

        using var source = new Bitmap(stream);
        var icon = new Bitmap(24, 24);
        using Graphics graphics = Graphics.FromImage(icon);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, 24, 24));
        return icon;
    }

    // Drops the cached emoji icon and clears GH's icon cache so the next paint rebuilds it — used
    // when the emoji changes (dedupe / load) or when a placed component gains its document.
    private void ResetEmojiIcon()
    {
        _iconBitmap = null;
        DestroyIconCache();
    }

    /// <summary>
    /// Opens the chat window, or brings the existing one to the front. Only one chat window
    /// exists session-wide: if it is already open, it is switched to view this Chatbox (the
    /// same as clicking this component's circle in the window's switcher row) and brought
    /// forward rather than torn down and reopened.
    /// </summary>
    public void OpenWindow()
    {
        if (_activeWindow is { } existing)
        {
            existing.SetActiveComponent(this);
            existing.BringToFront();
            existing.Focus();
            return;
        }

        var window = new ChatWindow(this);
        _activeWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeWindow, window))
            {
                _activeWindow = null;
            }
        };
        window.Show();
    }

    /// <summary>
    /// Submits a message from the window as a Prompt Signal: mints and latches a signal
    /// whose payload is the text (and whose content blocks carry any pasted/dropped
    /// images), then expires so the signal reaches the wire. Marshalled onto the UI
    /// thread because the bridge invokes it off the GH solve thread. An empty message
    /// (no text and no images) is ignored.
    /// </summary>
    /// <param name="text">The prompt text entered in the window; used as the signal payload.</param>
    /// <param name="contentBlocks">
    /// Interleaved text/image content blocks when the turn carries images, else null to
    /// use the plain text path (matches the classic Prompter contract).
    /// </param>
    public void SubmitFromWindow(string text, IReadOnlyList<MessageContent>? contentBlocks = null)
    {
        bool hasBlocks = contentBlocks is { Count: > 0 };
        if (string.IsNullOrWhiteSpace(text) && !hasBlocks)
        {
            return;
        }

        Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            LatchSuccess(text ?? string.Empty, contentBlocks: hasBlocks ? contentBlocks : null);
            ExpireSolution(true);
        }));
    }

    /// <summary>
    /// Notifies the chat window when this component is removed from the document. If the
    /// window is currently viewing this Chatbox it switches to another one still on the
    /// canvas, or closes if this was the last; a circle for an unrelated removed Chatbox
    /// simply drops out of the switcher row on the next tick.
    /// </summary>
    /// <param name="document">The document the component was removed from.</param>
    public override void RemovedFromDocument(GH_Document document)
    {
        _activeWindow?.OnComponentRemoved(this);
        base.RemovedFromDocument(document);
    }

    /// <summary>
    /// On first placement (not a file load), reassigns this Chatbox's emoji to one not already
    /// used by another Chatbox on the canvas, so freshly placed boxes are visually distinct.
    /// Falls back to the random pick when the palette is exhausted. A Chatbox restored from a
    /// file keeps its persisted emoji untouched.
    /// </summary>
    /// <param name="document">The document this component was added to.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        // Now that this instance has a document, flip its icon from the ribbon brain to the
        // blank canvas slot (the emoji is painted over it live).
        ResetEmojiIcon();

        if (_loaded)
        {
            return;
        }

        var used = new HashSet<string>();
        foreach (IGH_DocumentObject obj in document.Objects)
        {
            if (obj is Chatbox cb && !ReferenceEquals(cb, this) && !string.IsNullOrEmpty(cb._emoji))
            {
                used.Add(cb._emoji);
            }
        }

        if (used.Contains(_emoji))
        {
            string? free = OceanEmoji.FirstOrDefault(e => !used.Contains(e));
            if (free is not null)
            {
                _emoji = free;
            }
        }
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);

        // A Chatbox is a harness only once it owns members. While it has none, only the entry
        // items show; the collapse/expand and remove items appear once it is a harness.
        bool hasMembers = _group.Count > 0;

        // A Chatbox that is itself a member of another harness must not start or extend its own,
        // so harnesses can never nest — the entry items are greyed out in that case.
        bool isMember = IsMemberOfAnotherHarness();

        if (hasMembers)
        {
            Menu_AppendItem(menu, _group.Collapsed ? "Expand Harness" : "Collapse Harness", (_, _) => ToggleCollapse());
        }

        Menu_AppendItem(menu, "Collapse Selected into Harness", (_, _) => CollapseSelectedIntoHarness(), !isMember);
        Menu_AppendItem(menu, "Add Selected to Harness", (_, _) => AddSelectedToHarness(), !isMember);

        if (hasMembers)
        {
            Menu_AppendItem(menu, "Remove Selected from Harness", (_, _) => RemoveSelectedFromHarness());
        }
    }

    /// <summary>
    /// Toggles the collapsed state of the harness group, hiding or restoring its members.
    /// Driven by the canvas chevron/menu and the chat-window button.
    /// </summary>
    public void ToggleCollapse() => _group.Toggle();

    /// <summary>
    /// Collapses the harness on the next idle pass. Used right after a predefined workflow is
    /// placed, so the workflow lands collapsed — deferred so the placement's solution has settled
    /// and every member has been laid out (so native-member attribute swaps and the proxy pivot
    /// are valid) before the group is hidden.
    /// </summary>
    public void CollapseHarnessDeferred()
    {
        Rhino.RhinoApp.Idle -= CollapseOnIdle; // never stack handlers
        Rhino.RhinoApp.Idle += CollapseOnIdle;
    }

    /// <summary>
    /// Adds the document's currently selected objects (other than this Chatbox) to the harness
    /// group and collapses it — "collapse these into my Chatbox". The membership change is
    /// recorded for undo/redo.
    /// </summary>
    public void CollapseSelectedIntoHarness()
    {
        IReadOnlyList<Guid> added = _group.Add(SelectedGuids());
        RecordMembershipUndo(added, added: true);
        _group.SetCollapsed(true);
    }

    /// <summary>
    /// Adds the currently selected objects to the harness group without changing its collapsed
    /// state — "add to harness". Recorded for undo/redo.
    /// </summary>
    public void AddSelectedToHarness() => RecordMembershipUndo(_group.Add(SelectedGuids()), added: true);

    /// <summary>
    /// Removes the currently selected objects from the harness group, restoring any that were
    /// hidden. Recorded for undo/redo.
    /// </summary>
    public void RemoveSelectedFromHarness() => RecordMembershipUndo(_group.Remove(SelectedGuids()), added: false);

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        _group.Write(writer);
        writer.SetString("ChatboxEmoji", _emoji);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _group.Read(reader);
        _pendingApply = true;
        _loaded = true;

        string stored = string.Empty;
        if (reader.TryGetString("ChatboxEmoji", ref stored) && !string.IsNullOrEmpty(stored))
        {
            _emoji = stored;
            ResetEmojiIcon();
        }

        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override string MessageForState(SolveState state) => string.Empty;

    // Pushes a harness membership change onto Grasshopper's undo stack so it can be undone and
    // redone. The action reverses the exact delta through Group.Add/Remove. No-op for an empty
    // delta (e.g. nothing selected, or all already members).
    private void RecordMembershipUndo(IReadOnlyList<Guid> changed, bool added)
    {
        if (changed.Count == 0)
        {
            return;
        }

        GH_Document? doc = OnPingDocument();
        string name = added ? "Add to Harness" : "Remove from Harness";
        doc?.UndoServer.PushUndoRecord(name, new Physalia.GH.Harness.HarnessMembershipUndoAction(InstanceGuid, changed, added));
    }

    // Whether this Chatbox is itself a member of another Chatbox's harness — in which case it may
    // not start or extend its own (no nested harnesses).
    private bool IsMemberOfAnotherHarness()
    {
        GH_Document? doc = OnPingDocument();
        return doc is not null && HarnessGroup.IsMemberOfAnyHarness(doc, InstanceGuid, InstanceGuid);
    }

    // The selected document objects other than this Chatbox itself.
    private IReadOnlyList<Guid> SelectedGuids()
    {
        GH_Document? doc = OnPingDocument();
        if (doc is null)
        {
            return Array.Empty<Guid>();
        }

        return doc.SelectedObjects()
            .Where(o => o.InstanceGuid != InstanceGuid)
            .Select(o => o.InstanceGuid)
            .ToList();
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs — images are handled inside the window (paste/drop), not via a wire.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Prompt Signal", "PS", "Latched signal minted per sent message; its payload is the prompt text. Wire to Recorder's Prompt Signal.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        GH_Document? doc = OnPingDocument();
        if (doc is not null)
        {
            _group.Prune(doc);

            if (_pendingApply)
            {
                // Re-apply a loaded collapsed state once, deferred to idle so every member
                // has been added to the document and laid out first.
                _pendingApply = false;
                Rhino.RhinoApp.Idle += ApplyGroupOnIdle;
            }
        }

        EmitSignal(DA, 0, SuccessSignal);
    }

    private void ApplyGroupOnIdle(object? sender, EventArgs e)
    {
        Rhino.RhinoApp.Idle -= ApplyGroupOnIdle;
        _group.ApplyState();
    }

    private void CollapseOnIdle(object? sender, EventArgs e)
    {
        Rhino.RhinoApp.Idle -= CollapseOnIdle;
        _group.SetCollapsed(true);
    }
}
