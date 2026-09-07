// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Puts a button in the chat window that opens a page listing every trigger in this pipeline, with a
/// switch for each and one for all of them at once.
///
/// <para><b>Why arming needed somewhere better than a right-click menu.</b> A trigger's own menu is
/// the right home for one of them and stops being enough at three: they are scattered inside a
/// harness the user is usually not looking at, and arming is the act with a bill attached. Until this
/// existed, "what is switched on right now" had no answer short of visiting every node — and the
/// harness panel could only say how many were armed and switch them all off, which is a fire alarm
/// rather than a control.</para>
///
/// <para><b>Read live, never stored.</b> Which triggers exist is not a setting; it is whatever is on
/// the canvas at the moment the page is opened, so the window asks the Conversation Log, which scans
/// its own document. That is the "ask the component, not the solver" rule — a scan cannot go stale
/// the way a cached list would when somebody drops a Timer in while the window is open. It also means
/// this tool carries no state of its own, so nothing about it needs to survive a copy or ship in a
/// preset beyond the fact that it is wired.</para>
///
/// <para><b>Switching one off is not the same act as switching everything off</b>, and the page keeps
/// them apart. One named trigger goes through <c>SetArmedAndHandOver</c> — exactly what its own menu
/// does, so switching a Watch Modelling off from the list SENDS the recording, as it must. "Switch
/// all off" is the kill switch and discards, because somebody stopping everything is not asking for a
/// round to start. Reusing one verb for both would have made one of those two cases silently
/// wrong.</para>
/// </summary>
public class TriggerControl : HumanToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TriggerControl"/> class.
    /// </summary>
    public TriggerControl()
        : base(
            "Trigger Control",
            "Triggers",
            "Puts a button in the chat window listing every trigger in this pipeline, with a switch for each one and one for all of them. Wire into a Conversation Log's Human Tools input.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("2B5F71D8-46C3-4A09-95E7-08D1C36BA742");

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Puts the trigger list in the chat window. Wire into a Conversation Log's Human Tools input.";

    /// <inheritdoc/>
    protected override HumanTool Tool => new TriggerControlTool();

    /// <inheritdoc/>
    /// <remarks>
    /// Says on the node when the page it offers would be empty. Without it the failure is silent and
    /// off-canvas — the tool is wired, the button appears, and the page says "no triggers" with
    /// nowhere obvious to look. The same reason Token Count reports a missing link.
    /// </remarks>
    protected override void OnSolveEnd()
    {
        GH_Document? document = OnPingDocument();
        if (document is null)
        {
            return;
        }

        if (!document.Objects.OfType<IArmableTrigger>().Any())
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Remark,
                "There are no triggers in this pipeline yet, so the page this opens will be empty. "
                + "Add a Timer, Folder Watcher, Rhino Changed, Data Changed or Watch Modelling.");
        }
    }
}
