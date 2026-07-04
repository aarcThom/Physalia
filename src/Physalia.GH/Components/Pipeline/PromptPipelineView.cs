// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Read-only views of the prompt pipeline by wire-graph traversal, used by the Chat
/// window (the prompt entry point). A prompt source mints a
/// signal to a ConversationLog; the ConversationLog's Signal fans out — directly to a LlmCall, or through
/// one or more compaction components / gates that forward the Instructions-carrying signal on
/// toward a LlmCall. None of these links are inputs, so connecting a wire never re-solves;
/// callers refresh on every paint/tick. All reads are UI-thread safe.
/// </summary>
internal static class PromptPipelineView
{
    // Belt-and-suspenders cap on the forward walk (the visited set already prevents revisits).
    private const int MaxHops = 256;

    /// <summary>
    /// Finds the ConversationLog wired to the given output of a prompt source, or null when none.
    /// </summary>
    /// <param name="source">The prompt source component (Chat).</param>
    /// <param name="outputIndex">The Prompt Signal output index on the source.</param>
    /// <returns>The wired ConversationLog, or null.</returns>
    public static ConversationLog? FindConversationLog(IGH_Component source, int outputIndex)
    {
        foreach (IGH_Param recipient in source.Params.Output[outputIndex].Recipients)
        {
            if (recipient.Attributes?.GetTopLevel?.DocObject is ConversationLog conversationLog)
            {
                return conversationLog;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the ConversationLog itself is mid-run, or any lifecycle component on the forward signal
    /// spine (a compaction component / gate, or the LlmCall) is mid-run.
    /// </summary>
    /// <param name="conversationLog">The ConversationLog to inspect.</param>
    /// <returns>true while the pipeline is busy.</returns>
    public static bool IsPipelineBusy(ConversationLog conversationLog)
    {
        if (conversationLog.IsBusy)
        {
            return true;
        }

        foreach (IGH_Component comp in DownstreamSignalComponents(conversationLog))
        {
            if (comp is StatefulComponentBase { IsBusy: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cancels the active inference on every LlmCall on the forward signal spine of the given
    /// ConversationLog. Fired by the chat window's cancel button, which the UI enables only while the
    /// pipeline is busy. No-op when no LlmCall is running.
    /// </summary>
    /// <param name="conversationLog">The ConversationLog whose downstream LlmCall(s) to cancel.</param>
    public static void CancelPipeline(ConversationLog conversationLog)
    {
        foreach (IGH_Component comp in DownstreamSignalComponents(conversationLog))
        {
            if (comp is LlmCall llmCall)
            {
                llmCall.CancelInference();
            }
        }
    }

    /// <summary>
    /// Whether the prompt source feeds a complete inference pipeline: a ConversationLog is wired to the
    /// given output, and its Signal reaches a LlmCall — directly or through a compaction
    /// component / gate — that has a Model (LLM) connected. Used by the chat window to choose
    /// between the setup state and the normal chat state.
    /// </summary>
    /// <param name="source">The prompt source component (Chat).</param>
    /// <param name="outputIndex">The Prompt Signal output index on the source.</param>
    /// <returns>true when the ConversationLog -> [compactor…] -> LlmCall -> Model chain is fully wired.</returns>
    public static bool IsPipelineReady(IGH_Component source, int outputIndex)
    {
        ConversationLog? conversationLog = FindConversationLog(source, outputIndex);
        if (conversationLog is null)
        {
            return false;
        }

        foreach (IGH_Component comp in DownstreamSignalComponents(conversationLog))
        {
            if (comp is LlmCall llmCall && HasModelConnected(llmCall))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The partial response of the busy LlmCall on the forward signal spine, or null when
    /// nothing is streaming.
    /// </summary>
    /// <param name="conversationLog">The ConversationLog whose downstream LlmCall to peek.</param>
    /// <returns>The streaming text so far, or null.</returns>
    public static string? GetStreamingText(ConversationLog conversationLog)
    {
        foreach (IGH_Component comp in DownstreamSignalComponents(conversationLog))
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
    /// given ConversationLog, or null when no estimator is wired downstream of it — or when the
    /// estimator has not produced a count yet. Read from the estimator's Token Count output
    /// volatile data, so the value always matches what the canvas shows. Drives the chat
    /// window's token counter, which renders nothing on null.
    /// </summary>
    /// <param name="conversationLog">The ConversationLog at the head of the spine.</param>
    /// <returns>The estimated token count, or null.</returns>
    public static int? GetDownstreamTokenCount(ConversationLog conversationLog)
    {
        foreach (IGH_Component comp in DownstreamSignalComponents(conversationLog))
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
    /// Walks forward from the ConversationLog's Signal output along signal wires, yielding every
    /// component on the inference spine: the direct recipients and, hopping through each
    /// intermediary's own signal outputs, anything they forward to (compaction components,
    /// the Token Threshold gate, …) up to and including a LlmCall. The walk does not continue
    /// past a LlmCall (its outputs feed tools/feedback, not further inference). A visited set
    /// keyed on InstanceGuid keeps the wireless feedback loop from cycling.
    /// </summary>
    /// <param name="conversationLog">The ConversationLog at the head of the spine.</param>
    /// <returns>The downstream signal components, each yielded once.</returns>
    private static IEnumerable<IGH_Component> DownstreamSignalComponents(ConversationLog conversationLog)
    {
        IGH_Param? signal = ConversationLogSignalOutput(conversationLog);
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

            // Stop at a LlmCall; otherwise follow this component's signal outputs forward.
            if (comp is LlmCall)
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

    // The ConversationLog's outgoing Signal output, found by name so a future output re-order can't break
    // this (it used to be hard-coded to index 1; the ConversationLog is now signal-only at index 0).
    private static IGH_Param? ConversationLogSignalOutput(ConversationLog conversationLog)
    {
        foreach (IGH_Param output in conversationLog.Params.Output)
        {
            if (output.Name == "Signal")
            {
                return output;
            }
        }

        return conversationLog.Params.Output.Count > 0 ? conversationLog.Params.Output[0] : null;
    }

    // True when the LlmCall's Model input has at least one wired source (an LLM is connected).
    private static bool HasModelConnected(LlmCall llmCall)
    {
        foreach (IGH_Param input in llmCall.Params.Input)
        {
            if (input.Name == "Model")
            {
                return input.SourceCount > 0;
            }
        }

        return false;
    }
}
