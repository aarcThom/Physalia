// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Attributes;
using Physalia.GH.Panels;
using Physalia.GH.Parameters;

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
    // Only one chat window may exist per Rhino session, across every Chatbox instance.
    // Static so a second Chatbox takes over the single window rather than spawning another.
    // Session-only — nothing here serializes.
    private static ChatWindow? _activeWindow;
    private static Chatbox? _activeOwner;

    /// <summary>
    /// Initializes a new instance of the <see cref="Chatbox"/> class.
    /// </summary>
    public Chatbox()
        : base("Chatbox", "Chat", "Standalone chat window driving the pipeline. Double-click to open the window; send a message to mint a Prompt Signal.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B7E4B6F2-3C2A-4D71-9E0A-7F1C2D3E4A5B");

    /// <inheritdoc/>
    protected override string ClearMenuText => "Clear Signal";

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new ChatboxAttrib(this);
    }

    /// <summary>
    /// Opens the chat window, or brings the existing one to the front. Only one chat window
    /// exists session-wide: if another Chatbox already owns it, that window is closed and
    /// this component takes over. Idempotent for the owning component.
    /// </summary>
    public void OpenWindow()
    {
        if (_activeWindow is { } existing)
        {
            if (ReferenceEquals(_activeOwner, this))
            {
                existing.BringToFront();
                existing.Focus();
                return;
            }

            // A different Chatbox owns the single window — close it and take over.
            existing.Close();
        }

        var window = new ChatWindow(this);
        _activeWindow = window;
        _activeOwner = this;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeWindow, window))
            {
                _activeWindow = null;
                _activeOwner = null;
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
    /// Closes the chat window when this component is removed from the document, so the
    /// window never outlives the component that drives it.
    /// </summary>
    /// <param name="document">The document the component was removed from.</param>
    public override void RemovedFromDocument(GH_Document document)
    {
        if (ReferenceEquals(_activeOwner, this))
        {
            _activeWindow?.Close();
        }

        base.RemovedFromDocument(document);
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
        pManager.AddParameter(new Param_Signal(), "Prompt Signal", "PS", "Latched signal minted per sent message; its payload is the prompt text. Wire to Recorder's Prompt Signal.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        EmitSignal(DA, 0, SuccessSignal);
    }
}
