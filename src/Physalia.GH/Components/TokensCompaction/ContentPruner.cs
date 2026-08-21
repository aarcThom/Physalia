// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Compaction;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Components;

/// <summary>
/// Selective content pruning: removes or shortens the bulky, low-value parts of a conversation
/// (images, tool exchanges, auto-generated feedback, over-long tool output or text) while keeping
/// the conversational thread intact. Targets the biggest token sinks — stale tool results above
/// all — without forgetting whole turns. Deterministic; no LLM call. Tool pairing and role
/// alternation are repaired after pruning, so the output is always valid for replay.
/// </summary>
public class ContentPruner : CompactionComponentBase
{
    private const int InDropImages = 0;
    private const int InDropTools = 1;
    private const int InDropFeedback = 2;
    private const int InMaxToolResultChars = 3;
    private const int InMaxTextChars = 4;

    // How many trailing messages keep their document and plan block verbatim. Two covers the live
    // working set in a feedback loop — the model's current submission and the feedback answering
    // it — so a correction round still sees exactly what it is correcting, while everything the
    // canvas state has already absorbed is elided.
    private const int WorkingSetMessages = 2;

    // Menu toggles, not inputs. This is a shipped RoutingComponentBase subclass whose Signal input
    // is appended last by the base, so a new input at index 5 would shift the Signal to index 6 and
    // steal the signal wire in every saved document that already uses this component.
    private bool _elideStaleDocuments;
    private bool _stripStalePlanBlocks;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentPruner"/> class.
    /// </summary>
    public ContentPruner()
        : base(
            "Content Pruner",
            "Prune",
            "Shortens the conversation by throwing out the bulky parts rather than whole turns: pictures, finished tool exchanges, feedback already acted on, runaway text. Every turn survives; some just get lighter.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("EE741363-71D5-411A-AB19-51D58BF1D4FC");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The conversation to lighten, riding on a Conversation Log's signal. Usually reached from a Token Threshold's Over Limit output.";

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "The lightened conversation, ready for the LLM Call. If nothing can be pruned, the conversation goes on in full rather than the turn being lost.";

    /// <inheritdoc/>
    protected override void RegisterCompactionInputs(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter("Drop Images", "I", "Throw away the pictures. Usually the heaviest thing in the history by a wide margin.", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Drop Tool Exchanges", "X", "Throw away tool requests together with their answers. Always both, since a provider rejects one without the other.", GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Drop Feedback", "F", "Throw away the automatic feedback turns — reports and complaints the model has already dealt with.", GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Max Tool Result Chars", "TR", "Cut tool answers back to this many characters. 0 leaves them whole.", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Max Text Chars", "TX", "Cut text back to this many characters. 0 leaves it whole.", GH_ParamAccess.item, 0);
    }

    /// <inheritdoc/>
    protected override CompactionResult Compact(Instructions instructions, IGH_DataAccess da)
    {
        bool dropImages = false;
        bool dropTools = false;
        bool dropFeedback = false;
        int maxToolResultChars = 0;
        int maxTextChars = 0;

        da.GetData(InDropImages, ref dropImages);
        da.GetData(InDropTools, ref dropTools);
        da.GetData(InDropFeedback, ref dropFeedback);
        da.GetData(InMaxToolResultChars, ref maxToolResultChars);
        da.GetData(InMaxTextChars, ref maxTextChars);

        var options = new PruneOptions
        {
            DropImages = dropImages,
            DropToolExchanges = dropTools,
            DropFeedbackTurns = dropFeedback,
            MaxToolResultChars = maxToolResultChars > 0 ? maxToolResultChars : null,
            MaxTextChars = maxTextChars > 0 ? maxTextChars : null,
            StaleDocumentKeepLast = _elideStaleDocuments ? WorkingSetMessages : null,
            StalePlanBlockKeepLast = _stripStalePlanBlocks ? WorkingSetMessages : null,
        };

        return ConversationCompactor.Prune(instructions.Conversation, options);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Both toggles target the same thing from opposite ends: in a generate-place-measure loop the
    /// model's own past turns dominate the replayed window, and the canvas-state grounding already
    /// tells it what actually landed. Menu items rather than inputs — see the field note.
    /// </remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);

        Menu_AppendItem(
            menu,
            "Elide Stale Documents",
            (_, _) =>
            {
                _elideStaleDocuments = !_elideStaleDocuments;
                ExpireSolution(true);
            },
            enabled: true,
            @checked: _elideStaleDocuments);

        Menu_AppendItem(
            menu,
            "Strip Stale Plan Blocks",
            (_, _) =>
            {
                _stripStalePlanBlocks = !_stripStalePlanBlocks;
                ExpireSolution(true);
            },
            enabled: true,
            @checked: _stripStalePlanBlocks);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer)
    {
        writer.SetBoolean("ElideStaleDocuments", _elideStaleDocuments);
        writer.SetBoolean("StripStalePlanBlocks", _stripStalePlanBlocks);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        // Default OFF for a document saved before these existed: eliding history is a behaviour
        // change, and a reopened rig must not silently start rewriting what the model sees.
        _elideStaleDocuments = reader.ItemExists("ElideStaleDocuments") && reader.GetBoolean("ElideStaleDocuments");
        _stripStalePlanBlocks = reader.ItemExists("StripStalePlanBlocks") && reader.GetBoolean("StripStalePlanBlocks");
        return base.Read(reader);
    }
}
