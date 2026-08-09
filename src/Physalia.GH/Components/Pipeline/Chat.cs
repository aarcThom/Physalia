// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Attributes;
using Physalia.GH.Panels;
using Physalia.GH.Harness;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Standalone-window chat entry point — the pipeline's sole prompt source. A signal source
/// with one Prompt Signal output and no conversation state of its own; it drives a separate
/// Eto WebView window hosting a web chat UI. Each send from the window mints one Prompt Signal
/// whose payload is the prompt text — wire it to Conversation Log's Prompt Signal input.
/// </summary>
public class Chat : StatefulComponentBase
{
    // Single-glyph sea/ocean emojis used as a Chat's visual identity — shown in place of
    // its canvas icon and as its circle in the chat window's switcher row, so the user can
    // tell which Chat a given dot belongs to. Kept to plain single-codepoint glyphs (no
    // ZWJ sequences or variation selectors) so TextRenderer paints them predictably.
    private static readonly string[] OceanEmoji =
    {
        "🌊", "🐠", "🐟", "🐡", "🦈", "🐙", "🐚", "🦀", "🦞", "🦐",
        "🦑", "🐳", "🐋", "🐬", "🦭", "🐢", "🪼", "🐧", "🦦", "⚓", "🪸",
    };

    // Only one chat window may exist per Rhino session, across every Chat instance.
    // Static so a second Chat switches the single window to its own view rather than
    // spawning another. Session-only — nothing here serializes.
    private static ChatWindow? _activeWindow;

    // This Chat's assigned ocean emoji (its identity). Always non-empty — seeded randomly
    // in the constructor, deduped against canvas siblings on first placement, and persisted.
    private string _emoji;

    // The per-instance colour emoji icon (a bundled Noto bitmap scaled to 24x24); lazily built,
    // dropped when the emoji changes so the next Icon get rebuilds it.
    private Bitmap? _iconBitmap;

    /// <summary>
    /// Initializes a new instance of the <see cref="Chat"/> class.
    /// </summary>
    public Chat()
        : base("Chat", "Chat", "Standalone chat window driving the pipeline. Double-click to open the window; send a message to mint a Prompt Signal.", "Pipeline")
    {
        _emoji = OceanEmoji[Random.Shared.Next(OceanEmoji.Length)];
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B7E4B6F2-3C2A-4D71-9E0A-7F1C2D3E4A5B");

    /// <summary>
    /// Gets the ocean emoji that identifies this Chat on the canvas (as its icon) and in
    /// the chat window's switcher row. Stable for the life of the component and persisted.
    /// </summary>
    public string Emoji => _emoji;

    /// <inheritdoc/>
    protected override string ClearMenuText => "Clear Signal";

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new ChatAttrib(this);
    }

    /// <summary>
    /// Gets the component icon — this Chat's assigned ocean emoji as a bundled colour bitmap
    /// (Noto Emoji), so each Chat reads as a distinct node. GDI cannot colour-render an emoji
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
    /// exists session-wide: if it is already open, it is switched to view this Chat (the
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
    /// use the plain text path.
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
    /// Sends a viewport snapshot of the transmitter-generated geometry as its own user message —
    /// fired by the chat window's geometry button, never automatically. Captures the viewport
    /// (framed on the generated geometry) and mints one Prompt Signal whose turn carries the
    /// Geometry Snapshot tool's message plus the snapshot image, exactly like a typed message
    /// with an attached image. Quietly does nothing when the tool is unwired, no generated
    /// geometry exists, or the capture fails — the button only shows while the first two hold.
    /// Marshalled onto the UI thread (the bridge invokes it off the GH solve thread), where the
    /// viewport zoom + capture are safe between solves (the same operations Geometry Observation
    /// defers to <c>RhinoApp.Idle</c> for, because it captures from inside a solve).
    /// </summary>
    public void SendGeometrySnapshotFromWindow()
    {
        Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(this, 0);
            if (conversationLog is null || !TryCaptureGeneratedGeometryPng(out byte[]? imageBytes) || imageBytes is null)
            {
                return;
            }

            LatchSnapshotTurn(conversationLog.GeometrySnapshotMessage, imageBytes);
        }));
    }

    /// <summary>
    /// Sends a capture of the active Rhino viewport as its own user message — fired by the chat
    /// window's view button, never automatically. The geometry-free counterpart of
    /// <see cref="SendGeometrySnapshotFromWindow"/>: no generated-geometry scan and no camera move, so
    /// what the model receives is exactly what the human is looking at. Quietly does nothing when the
    /// View Snapshot tool is unwired or the capture fails. Marshalled onto the UI thread (the bridge
    /// invokes it off the GH solve thread), where a viewport capture is safe between solves.
    /// </summary>
    public void SendViewSnapshotFromWindow()
    {
        Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(this, 0);
            if (conversationLog is null || !TryCaptureViewPng(out byte[]? imageBytes) || imageBytes is null)
            {
                return;
            }

            LatchSnapshotTurn(conversationLog.ViewSnapshotMessage, imageBytes);
        }));
    }

    /// <summary>
    /// Captures a viewport snapshot of the transmitter-generated geometry and hands back the PNG
    /// bytes without minting anything — the attach half of the geometry button, used when the wired
    /// Geometry Snapshot tool has "Send With Default Message" unchecked. The image is pushed into the
    /// chat window's prompt box like a pasted attachment and leaves on the human's own turn, so no
    /// signal is latched and no solve is expired here.
    /// </summary>
    /// <param name="png">The captured PNG bytes, or null when there is nothing to capture.</param>
    /// <returns>True when a snapshot was captured.</returns>
    public bool TryCaptureGeneratedGeometryPng(out byte[]? png)
    {
        png = null;
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(this, 0);
        if (conversationLog is null || !conversationLog.HasGeometrySnapshotTool)
        {
            return false;
        }

        Rhino.Geometry.BoundingBox bounds = Generation.GeneratedGeometryScan.ComputeBounds(PhyDocuments.Host(this));
        if (!bounds.IsValid)
        {
            return false;
        }

        return Generation.ViewportSnapshot.TryCapture(bounds, out png, out _) && png is not null;
    }

    /// <summary>
    /// Captures the active Rhino viewport as-is and hands back the PNG bytes without minting anything —
    /// the attach half of the view button, used when the wired View Snapshot tool has "Send With Default
    /// Message" unchecked. Nothing on the canvas is inspected and the camera is never moved: an unset
    /// bounding box tells the shared capture to skip its zoom, so the human gets the frame they composed.
    /// </summary>
    /// <param name="png">The captured PNG bytes, or null when there is nothing to capture.</param>
    /// <returns>True when a capture was taken.</returns>
    public bool TryCaptureViewPng(out byte[]? png)
    {
        png = null;
        ConversationLog? conversationLog = PromptPipelineView.FindConversationLog(this, 0);
        if (conversationLog is null || !conversationLog.HasViewSnapshotTool)
        {
            return false;
        }

        return Generation.ViewportSnapshot.TryCapture(Rhino.Geometry.BoundingBox.Unset, out png, out _) && png is not null;
    }

    // Mints one Prompt Signal whose turn is the snapshot's message plus the captured image, exactly
    // like a typed message with an attached image, and expires so the signal reaches the wire. Shared
    // by both snapshot tools — all that differs between them is what was captured and which message
    // rides along. An empty message sends the image alone.
    private void LatchSnapshotTurn(string message, byte[] png)
    {
        var blocks = new List<MessageContent>();
        if (!string.IsNullOrWhiteSpace(message))
        {
            blocks.Add(new TextContent(message));
        }

        blocks.Add(new ImageContent(new InlineImage(png, "image/png")));

        LatchSuccess(message, contentBlocks: blocks);
        ExpireSolution(true);
    }

    /// <summary>
    /// Notifies the chat window when this component is removed from the document. If the
    /// window is currently viewing this Chat it switches to another one still on the
    /// canvas, or closes if this was the last; a circle for an unrelated removed Chat
    /// simply drops out of the switcher row on the next tick.
    /// </summary>
    /// <param name="document">The document the component was removed from.</param>
    public override void RemovedFromDocument(GH_Document document)
    {
        _activeWindow?.OnComponentRemoved(this);
        base.RemovedFromDocument(document);
    }

    /// <summary>
    /// Reassigns this Chat's emoji to one not already used by another Chat on the canvas,
    /// so placed boxes are visually distinct. The reassign is collision-based, so a clean file
    /// load (where every persisted emoji is already unique) leaves persisted emojis untouched,
    /// while an on-canvas duplicate (copy-paste / duplicate, which also runs through Read) is
    /// reshuffled because it collides with its source. Falls back to the existing pick when the
    /// palette is exhausted.
    /// </summary>
    /// <param name="document">The document this component was added to.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        // Now that this instance has a document, flip its icon from the ribbon brain to the
        // blank canvas slot (the emoji is painted over it live).
        ResetEmojiIcon();

        var used = new HashSet<string>();
        foreach (IGH_DocumentObject obj in document.Objects)
        {
            if (obj is Chat cb && !ReferenceEquals(cb, this) && !string.IsNullOrEmpty(cb._emoji))
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

                // The top-of-method reset ran before _emoji changed, so rebuild the icon now.
                ResetEmojiIcon();
            }
        }
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("ChatboxEmoji", _emoji);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
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

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs — images are handled inside the window (paste/drop), not via a wire.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Prompt Signal", "PS", "Latched signal minted per sent message; its payload is the prompt text. Wire to Conversation Log's Prompt Signal.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        EmitSignal(DA, 0, SuccessSignal);
    }
}
