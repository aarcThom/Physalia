// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tools;

/// <summary>
/// One group of tool calls that share a dispatch target: every call whose name matches a single
/// router output, carried together because one output emits one latched signal (parallel calls to
/// the same tool ride as one dispatch, and the tool node returns a result per call).
/// </summary>
/// <param name="OutputName">The matched output name (the call payloads are sent here).</param>
/// <param name="Calls">The calls routed to this output, in encounter order.</param>
/// <param name="Payload">The newline-joined input JSON of the grouped calls (the dispatch payload).</param>
public sealed record ToolDispatchGroup(
    string OutputName,
    IReadOnlyList<ToolCallContent> Calls,
    string Payload);

/// <summary>
/// The plan for dispatching one assistant turn's tool calls: how the calls group onto outputs, the
/// ids whose results must be gathered before the round completes, synthetic error results for calls
/// with no matching output, and any warnings to surface.
/// </summary>
/// <param name="Groups">The per-output dispatch groups, in encounter order.</param>
/// <param name="PendingToolUseIds">The ids of dispatched calls whose results are awaited.</param>
/// <param name="SyntheticErrorResults">Error results for calls that had no matching output.</param>
/// <param name="Warnings">Human-readable warnings to surface.</param>
public sealed record ToolDispatchPlan(
    IReadOnlyList<ToolDispatchGroup> Groups,
    IReadOnlyList<string> PendingToolUseIds,
    IReadOnlyList<ToolResultContent> SyntheticErrorResults,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Pure dispatch policy for the Router. It decides how a LLM Call's tool calls map onto the router's
/// named outputs and how the returned results combine into the single tool-result turn a provider
/// requires after a multi-tool assistant turn. It mints no signals and touches no Grasshopper state:
/// the host supplies the available output names, mints signals from the returned plan, and combines
/// results via <see cref="CombineResults"/>.
/// </summary>
public static class ToolDispatchRound
{
    /// <summary>
    /// Plans how the given tool calls dispatch onto the available outputs. Calls whose name matches
    /// an output (case-insensitively) are grouped onto it; a call with no matching output is answered
    /// with a synthetic error result so the provider's "one tool_result per tool_use" rule still holds.
    /// </summary>
    /// <param name="calls">The tool calls from the assistant turn, in order.</param>
    /// <param name="availableOutputNames">The router's tool-output names (excluding Feedback).</param>
    /// <returns>The dispatch plan.</returns>
    public static ToolDispatchPlan Plan(
        IReadOnlyList<ToolCallContent> calls,
        IReadOnlyList<string> availableOutputNames)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(availableOutputNames);

        var orderedNames = new List<string>();
        var groupedCalls = new Dictionary<string, List<ToolCallContent>>(StringComparer.OrdinalIgnoreCase);
        var pendingIds = new List<string>();
        var syntheticErrors = new List<ToolResultContent>();
        var warnings = new List<string>();

        // The names the model may actually call, for the error given to an unmatched call so it can
        // correct itself (e.g. a model that invents "fetch_url" is told the real tool is "read_url").
        string availableList = availableOutputNames.Count > 0
            ? "The available tools are: " + string.Join(", ", availableOutputNames) + "."
            : "No tools are available.";

        foreach (ToolCallContent call in calls)
        {
            string? matchedName = null;
            foreach (string name in availableOutputNames)
            {
                if (string.Equals(name, call.Name, StringComparison.OrdinalIgnoreCase))
                {
                    matchedName = name;
                    break;
                }
            }

            if (matchedName is null)
            {
                warnings.Add(
                    $"No output is named \"{call.Name}\" — wire an output into that tool node, or the call is answered with an error.");

                // The provider requires a tool_result for EVERY tool_use; answer the undispatchable
                // call with an error result so the round can still complete with a valid pairing. Name
                // the available tools so the model corrects instead of re-calling the invented name.
                syntheticErrors.Add(new ToolResultContent(
                    call.Id,
                    $"The tool \"{call.Name}\" does not exist. {availableList} Call one of those instead — do not invent tool names.",
                    IsError: true));
                continue;
            }

            pendingIds.Add(call.Id);
            if (!groupedCalls.TryGetValue(matchedName, out List<ToolCallContent>? group))
            {
                group = new List<ToolCallContent>();
                groupedCalls[matchedName] = group;
                orderedNames.Add(matchedName);
            }

            group.Add(call);
        }

        var groups = new List<ToolDispatchGroup>(orderedNames.Count);
        foreach (string name in orderedNames)
        {
            List<ToolCallContent> group = groupedCalls[name];
            string payload = string.Join(Environment.NewLine, group.Select(c => c.InputJson));
            groups.Add(new ToolDispatchGroup(name, group, payload));
        }

        return new ToolDispatchPlan(groups, pendingIds, syntheticErrors, warnings);
    }

    /// <summary>
    /// Combines the collected tool results into the content blocks and trace payload for the single
    /// user turn forwarded to the Conversation Log after the assistant tool_use turn.
    /// </summary>
    /// <param name="results">The collected tool results, in arrival order.</param>
    /// <returns>The content blocks and the newline-joined non-blank result text.</returns>
    public static (IReadOnlyList<MessageContent> Blocks, string Payload) CombineResults(
        IReadOnlyList<ToolResultContent> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var blocks = results.Cast<MessageContent>().ToList();
        string payload = string.Join(
            Environment.NewLine,
            results.Select(r => r.Content).Where(StringHelpers.IsNonBlank));

        return (blocks, payload);
    }
}
