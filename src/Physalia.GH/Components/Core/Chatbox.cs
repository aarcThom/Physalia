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
    // Static so a second Chatbox switches the single window to its own view rather than
    // spawning another. Session-only — nothing here serializes.
    private static ChatWindow? _activeWindow;

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
