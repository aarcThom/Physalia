// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Read-only views of the prompt pipeline by wire-graph traversal, shared by the prompt
/// entry points (Prompter's canvas panel and Chatbox's window). A prompt source mints a
/// signal to a Recorder; the Recorder's Signal fans out — directly to a Reasoner, or through
/// one or more compaction components / gates that forward the Instructions-carrying signal on
/// toward a Reasoner. None of these links are inputs, so connecting a wire never re-solves;
/// callers refresh on every paint/tick. All reads are UI-thread safe.
/// </summary>
internal static class PromptPipelineView
{
    // Belt-and-suspenders cap on the forward walk (the visited set already prevents revisits).
    private const int MaxHops = 256;

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
    /// Whether the Recorder itself is mid-run, or any lifecycle component on the forward signal
    /// spine (a compaction component / gate, or the Reasoner) is mid-run.
    /// </summary>
    /// <param name="recorder">The Recorder to inspect.</param>
    /// <returns>true while the pipeline is busy.</returns>
    public static bool IsPipelineBusy(Recorder recorder)
    {
        if (recorder.IsBusy)
        {
            return true;
        }

        foreach (IGH_Component comp in DownstreamSignalComponents(recorder))
        {
            if (comp is StatefulComponentBase { IsBusy: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cancels the active inference on every Reasoner on the forward signal spine of the given
    /// Recorder. Fired by the chat window's cancel button, which the UI enables only while the
    /// pipeline is busy. No-op when no Reasoner is running.
    /// </summary>
    /// <param name="recorder">The Recorder whose downstream Reasoner(s) to cancel.</param>
    public static void CancelPipeline(Recorder recorder)
    {
        foreach (IGH_Component comp in DownstreamSignalComponents(recorder))
        {
            if (comp is Reasoner reasoner)
            {
                reasoner.CancelInference();
            }
        }
    }

    /// <summary>
    /// Whether the prompt source feeds a complete inference pipeline: a Recorder is wired to the
    /// given output, and its Signal reaches a Reasoner — directly or through a compaction
    /// component / gate — that has a Model (LLM) connected. Used by the chat window to choose
    /// between the setup state and the normal chat state.
    /// </summary>
    /// <param name="source">The prompt source component (Prompter or Chatbox).</param>
    /// <param name="outputIndex">The Prompt Signal output index on the source.</param>
    /// <returns>true when the Recorder -> [compactor…] -> Reasoner -> Model chain is fully wired.</returns>
    public static bool IsPipelineReady(IGH_Component source, int outputIndex)
    {
        Recorder? recorder = FindRecorder(source, outputIndex);
        if (recorder is null)
        {
            return false;
        }

        foreach (IGH_Component comp in DownstreamSignalComponents(recorder))
        {
            if (comp is Reasoner reasoner && HasModelConnected(reasoner))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The partial response of the busy Reasoner on the forward signal spine, or null when
    /// nothing is streaming.
    /// </summary>
    /// <param name="recorder">The Recorder whose downstream Reasoner to peek.</param>
    /// <returns>The streaming text so far, or null.</returns>
    public static string? GetStreamingText(Recorder recorder)
    {
        foreach (IGH_Component comp in DownstreamSignalComponents(recorder))
        {
            if (comp is IStreamingTextSource source
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

    /// <summary>
    /// The current estimate of the first Token Estimator on the forward signal spine of the
    /// given Recorder, or null when no estimator is wired downstream of it — or when the
    /// estimator has not produced a count yet. Read from the estimator's Token Count output
    /// volatile data, so the value always matches what the canvas shows. Drives the chat
    /// window's token counter, which renders nothing on null.
    /// </summary>
    /// <param name="recorder">The Recorder at the head of the spine.</param>
    /// <returns>The estimated token count, or null.</returns>
    public static int? GetDownstreamTokenCount(Recorder recorder)
    {
        foreach (IGH_Component comp in DownstreamSignalComponents(recorder))
        {
            if (comp is TokenEstimator estimator)
            {
                return ReadTokenCount(estimator);
            }
        }

        return null;
    }

    // Reads the integer on the estimator's Token Count output, or null when it holds no data.
    private static int? ReadTokenCount(TokenEstimator estimator)
    {
        if (estimator.Params.Output.Count == 0)
        {
            return null;
        }

        foreach (IGH_Goo goo in estimator.Params.Output[0].VolatileData.AllData(true))
        {
            if (goo is GH_Integer integer)
            {
                return integer.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Walks forward from the Recorder's Signal output along signal wires, yielding every
    /// component on the inference spine: the direct recipients and, hopping through each
    /// intermediary's own signal outputs, anything they forward to (compaction components,
    /// the Token Threshold gate, …) up to and including a Reasoner. The walk does not continue
    /// past a Reasoner (its outputs feed tools/feedback, not further inference). A visited set
    /// keyed on InstanceGuid keeps the wireless feedback loop from cycling.
    /// </summary>
    /// <param name="recorder">The Recorder at the head of the spine.</param>
    /// <returns>The downstream signal components, each yielded once.</returns>
    private static IEnumerable<IGH_Component> DownstreamSignalComponents(Recorder recorder)
    {
        IGH_Param? signal = RecorderSignalOutput(recorder);
        if (signal is null)
        {
            yield break;
        }

        var visited = new HashSet<Guid>();
        var queue = new Queue<IGH_Component>();
        Enqueue(signal, queue, visited);

        int hops = 0;
        while (queue.Count > 0 && hops++ < MaxHops)
        {
            IGH_Component comp = queue.Dequeue();
            yield return comp;

            // Stop at a Reasoner; otherwise follow this component's signal outputs forward.
            if (comp is Reasoner)
            {
                continue;
            }

            foreach (IGH_Param output in comp.Params.Output)
            {
                if (output is Param_Signal)
                {
                    Enqueue(output, queue, visited);
                }
            }
        }
    }

    // Enqueues the unvisited component recipients of a signal output.
    private static void Enqueue(IGH_Param signalOutput, Queue<IGH_Component> queue, HashSet<Guid> visited)
    {
        foreach (IGH_Param recipient in signalOutput.Recipients)
        {
            if (recipient.Attributes?.GetTopLevel?.DocObject is IGH_Component comp && visited.Add(comp.InstanceGuid))
            {
                queue.Enqueue(comp);
            }
        }
    }

    // The Recorder's outgoing Signal output, found by name so a future output re-order can't break
    // this (it used to be hard-coded to index 1; the Recorder is now signal-only at index 0).
    private static IGH_Param? RecorderSignalOutput(Recorder recorder)
    {
        foreach (IGH_Param output in recorder.Params.Output)
        {
            if (output.Name == "Signal")
            {
                return output;
            }
        }

        return recorder.Params.Output.Count > 0 ? recorder.Params.Output[0] : null;
    }

    // True when the Reasoner's Model input has at least one wired source (an LLM is connected).
    private static bool HasModelConnected(Reasoner reasoner)
    {
        foreach (IGH_Param input in reasoner.Params.Input)
        {
            if (input.Name == "Model")
            {
                return input.SourceCount > 0;
            }
        }

        return false;
    }
}
