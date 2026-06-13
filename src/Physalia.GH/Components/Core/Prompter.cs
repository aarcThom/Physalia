// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Attributes;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Entry point for user input; owns the chat UI. A panel-style component whose upper
/// section displays the wired Recorder's active conversation and whose lower section
/// accepts prompt text (double-click to type, Shift+Enter to submit). Each submit mints
/// one Prompt Signal whose payload is the prompt text — wire it to Recorder's Prompt
/// Signal input. The Prompter owns no conversation state of its own.
/// </summary>
public class Prompter : StatefulComponentBase
{
    /// <summary>
    /// The draft prompt text shown in the input section while the user is not actively
    /// typing. Persisted across save/reload; cleared on submit.
    /// </summary>
    public string UserPromptText = string.Empty;

    // Alias → image source, refreshed every solve from the Image Sources input so submit
    // (which fires from the UI, outside a solve) can resolve "/<alias>" references.
    private IReadOnlyDictionary<string, ImageSource> _imagesByAlias =
        new Dictionary<string, ImageSource>();

    /// <summary>
    /// Initializes a new instance of the <see cref="Prompter"/> class.
    /// </summary>
    public Prompter()
        : base("Prompter", "Prm", "Chat UI entry point. Double-click the input panel to type; Shift+Enter submits the prompt as a signal.", "Core")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("0859E274-56C3-4CD4-9E99-780225251EBF");

    /// <inheritdoc/>
    protected override string ClearMenuText => "Clear Signal";

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new PrompterAttrib(this);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ImageSource(), "Image Sources", "Img", "Optional images (from Image Gatherer) referenceable in the prompt with \"/<alias>\".", GH_ParamAccess.list);
        pManager[0].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Signal(), "Prompt Signal", "PS", "Latched signal minted per submitted prompt; its payload is the prompt text. Wire to Recorder's Prompt Signal.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Refresh the alias map every solve so submit (which fires from the UI) sees the
        // current Image Gatherer contents. Upstream edits re-solve this component.
        var images = new List<GH_ImageSource>();
        DA.GetDataList(0, images);

        var map = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        foreach (GH_ImageSource? goo in images)
        {
            if (goo?.Value is not { } resource || string.IsNullOrEmpty(resource.Alias))
            {
                continue;
            }

            if (map.ContainsKey(resource.Alias))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Duplicate image alias \"{resource.Alias}\" — the last one wins.");
            }

            map[resource.Alias] = resource.Source;
        }

        _imagesByAlias = map;

        EmitSignal(DA, 0, SuccessSignal);
    }

    /// <inheritdoc/>
    /// <remarks>The chat panel is its own status display — no caption under the component.</remarks>
    protected override string MessageForState(SolveState state) => string.Empty;

    /// <summary>
    /// Submits the current <see cref="UserPromptText"/> as a Prompt Signal: mints and
    /// latches a signal whose payload is the text, clears the draft, and expires the
    /// solution so the signal reaches the wire. Does nothing when the draft is blank.
    /// Called by <see cref="PrompterAttrib"/> on Shift+Enter. A source, not a hop —
    /// no visible Active delay.
    /// </summary>
    public void SubmitUserMessage()
    {
        if (string.IsNullOrWhiteSpace(UserPromptText))
        {
            return;
        }

        // Resolve "/<alias>" references into interleaved text/image content blocks. The plain
        // text becomes the signal payload (trace/display); the blocks carry the images.
        ResolvedPrompt resolved = PromptImageResolver.Resolve(UserPromptText, _imagesByAlias);

        LatchSuccess(resolved.Text, contentBlocks: resolved.Blocks);
        UserPromptText = string.Empty;
        ExpireSolution(true);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("PromptText", UserPromptText ?? string.Empty);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        string promptText = string.Empty;
        if (reader.TryGetString("PromptText", ref promptText))
        {
            UserPromptText = promptText;
        }

        return base.Read(reader);
    }
}
