// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using Grasshopper.Kernel;

namespace Physalia.GH.Components;

/// <summary>
/// Read-only views of the prompt pipeline by wire-graph traversal, shared by the prompt
/// entry points (Prompter's canvas panel and Chatbox's window). A prompt source mints a
/// signal to a Recorder; the Recorder fans out to the Reasoner. None of these links are
/// inputs, so connecting a wire never re-solves — callers refresh on every paint/tick.
/// All reads are UI-thread safe (geometry of the document graph plus volatile flags).
/// </summary>
internal static class PromptPipelineView
{
    /// <summary>
    /// Finds the Recorder wired to the given output of a prompt source, or null when none.
    /// </summary>
    /// <param name="source">The prompt source component (Prompter or Chatbox).</param>
    /// <param name="outputIndex">The Prompt Signal output index on the source.</param>
    /// <returns>The wired Recorder, or null.</returns>
    public static Recorder? FindRecorder(IGH_Component source, int outputIndex)
    {
        foreach (IGH_Param recipient in source.Params.Output[outputIndex].Recipients)
        {
            if (recipient.Attributes?.GetTopLevel?.DocObject is Recorder recorder)
            {
                return recorder;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the Recorder itself is mid-run, or any lifecycle component consuming its
    /// outgoing Signal (i.e. the Reasoner) is mid-run.
    /// </summary>
    /// <param name="recorder">The Recorder to inspect.</param>
    /// <returns>true while the pipeline is busy.</returns>
    public static bool IsPipelineBusy(Recorder recorder)
    {
        if (recorder.IsBusy)
        {
            return true;
        }

        foreach (IGH_Param recipient in recorder.Params.Output[1].Recipients)
        {
            if (recipient.Attributes?.GetTopLevel?.DocObject is StatefulComponentBase { IsBusy: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The partial response of the busy component (Reasoner) wired to the Recorder, or null
    /// when nothing is streaming.
    /// </summary>
    /// <param name="recorder">The Recorder whose Reasoner to peek.</param>
    /// <returns>The streaming text so far, or null.</returns>
    public static string? GetStreamingText(Recorder recorder)
    {
        foreach (IGH_Param recipient in recorder.Params.Output[1].Recipients)
        {
            if (recipient.Attributes?.GetTopLevel?.DocObject is IStreamingTextSource source
                && source is StatefulComponentBase { IsBusy: true })
            {
                string? text = source.StreamingText;
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        return null;
    }
}
