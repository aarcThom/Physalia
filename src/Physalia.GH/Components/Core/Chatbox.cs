// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Grasshopper.Kernel;
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
    // The chat window owned by this component instance; null when closed. Session-only.
    private ChatWindow? _window;

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
    /// Opens the chat window, or brings the existing one to the front. Idempotent.
    /// </summary>
    public void OpenWindow()
    {
        if (_window is { } existing)
        {
            existing.BringToFront();
            existing.Focus();
            return;
        }

        _window = new ChatWindow(this);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    /// <summary>
    /// Submits text from the window as a Prompt Signal: mints and latches a signal whose
    /// payload is the text, then expires so the signal reaches the wire. Marshalled onto
    /// the UI thread because the bridge may invoke it off the GH solve thread. Blank text
    /// is ignored.
    /// </summary>
    /// <param name="text">The prompt text entered in the window.</param>
    public void SubmitFromWindow(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            LatchSuccess(text);
            ExpireSolution(true);
        }));
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
