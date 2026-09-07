// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Components;

/// <summary>
/// A handful of named values the model keeps and the GRAPH can read. What makes the model's own
/// account of where it has got to something a pipeline can branch on rather than prose somebody has
/// to re-parse.
///
/// <para><b>Not the Memory tool, and the distinction is what it is for.</b> Memory is files of prose
/// the model writes for its future self, and the pipeline never looks inside them. This is the other
/// direction: the model writes <c>stage = 3</c> or <c>system = glulam</c>, and the value comes out on
/// a wire — into a comparison, into a Signal Gate, into a panel a person is watching. Before this,
/// the only structured state the graph could act on was the Build Plan tracker's, which parses one
/// specific block out of one specific kind of reply.</para>
///
/// <para><b>Session-only, per harness.</b> See <see cref="StateStore"/>. Working state belongs to the
/// run that produced it; the conversation is what persists, and the model's notes are what it keeps.
/// Two of these nodes in one harness share one board, which is how a value can be set beside the
/// model and read on the far side of the pipeline.</para>
///
/// <para><b>The Instruction input is the part worth filling in.</b> A model that has not been told
/// which keys this pipeline cares about will invent its own, and the gate downstream will be watching
/// a key nobody ever set. Whatever is typed there rides in the prompt — before the model decides
/// whether to call anything — rather than in the tool description, which it reads only once it is
/// already weighing the call.</para>
/// </summary>
public class PipelineState : LlmToolComponentBase
{
    private const int InKey = 1;
    private const int InInstruction = 2;

    private static readonly int OutKeys = FirstAdditionalOutputIndex;
    private static readonly int OutValues = FirstAdditionalOutputIndex + 1;
    private static readonly int OutValue = FirstAdditionalOutputIndex + 2;

    private static readonly LlmToolDefinition ToolDef = new(
        "state",
        "Keep and read a few named values that the Grasshopper definition can act on — which stage you are on, which option was chosen, the last measurement you took. Use \"set\" to record one, \"get\" to read one back, \"list\" to see them all, and \"clear\" to remove one (or all of them, with no key). These are short values the graph branches on, not a place to store working notes — use the memory tool for those.",
        "{\"type\":\"object\",\"properties\":"
        + "{\"action\":{\"type\":\"string\",\"enum\":[\"set\",\"get\",\"list\",\"clear\"],\"description\":\"What to do.\"},"
        + "\"key\":{\"type\":\"string\",\"description\":\"The name of the value. Required for set and get; for clear, omitting it removes every value.\"},"
        + "\"value\":{\"type\":\"string\",\"description\":\"The value to record. Required for set. Keep it short — a number, a word, a name.\"}},"
        + "\"required\":[\"action\"]}");

    private string _key = string.Empty;

    private string _instruction = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineState"/> class.
    /// </summary>
    public PipelineState()
        : base(
            "Pipeline State",
            "State",
            "Lets the model keep a few named values, and puts them on wires the definition can read. Use it to branch on what the model says it has done — wire Value into a comparison and that into a Signal Gate.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A470E36B-1D95-42C8-8F03-95B71E6C24AD");

    /// <inheritdoc/>
    /// <remarks>
    /// Only what the graph's author typed. Unlike the Memory tool's directive this does NOT make
    /// calling the tool mandatory: a pipeline that does not branch on state has no use for it, and
    /// telling every model to record its progress whether or not anything reads it is how a prompt
    /// fills up with instructions nobody needs.
    /// </remarks>
    public override string? GroundingDirective =>
        string.IsNullOrWhiteSpace(_instruction) ? null : _instruction.Trim();

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "A state read or write the model has asked for, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises the state board to the model. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "The confirmation or the value, heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => ToolDef;

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Key",
            "K",
            "Which value to put on the Value output. Leave blank to read the whole board off Keys and Values instead.",
            GH_ParamAccess.item,
            string.Empty);
        pManager.AddTextParameter(
            "Instruction",
            "I",
            "What to tell the model about this pipeline's state — which keys it should keep and what they mean. Rides in the prompt, so it is read before the model decides whether to record anything. Worth filling in: a model that is not told will invent its own keys, and the gate downstream will be watching one nobody set.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[InKey].Optional = true;
        pManager[InInstruction].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Keys",
            "K",
            "Every key on the board, most recently set last.",
            GH_ParamAccess.list);
        pManager.AddTextParameter(
            "Values",
            "V",
            "The matching values, in the same order.",
            GH_ParamAccess.list);
        pManager.AddTextParameter(
            "Value",
            "Val",
            "The value of the key on the Key input, or nothing when it is not set. This is the one to wire into a comparison and a Signal Gate.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        string key = string.Empty;
        string instruction = string.Empty;
        da.GetData(InKey, ref key);
        da.GetData(InInstruction, ref instruction);
        _key = key ?? string.Empty;
        _instruction = instruction ?? string.Empty;
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        // From OnSolveEnd, not OnSolveTick: a call in this very solve may have changed the board, and
        // OnSolveTick runs before the calls. Re-published unconditionally so the values stay on the
        // wire between solves.
        IReadOnlyList<KeyValuePair<string, string>> board = StateStore.All(OnPingDocument());

        da.SetDataList(OutKeys, board.Select(p => p.Key));
        da.SetDataList(OutValues, board.Select(p => p.Value));
        da.SetData(OutValue, StateStore.Get(OnPingDocument(), _key) ?? string.Empty);

        Message = board.Count switch
        {
            0 => string.Empty,
            1 => "1 value",
            _ => $"{board.Count} values",
        };
    }

    /// <inheritdoc/>
    protected override ToolCallResult ExecuteCall(ToolCallContent call)
    {
        string action;
        string key;
        string value;

        try
        {
            using JsonDocument args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.InputJson) ? "{}" : call.InputJson);
            action = Str(args.RootElement, "action");
            key = Str(args.RootElement, "key");
            value = Str(args.RootElement, "value");
        }
        catch (JsonException ex)
        {
            return ToolCallResult.Error($"The arguments were not valid JSON: {ex.Message}");
        }

        GH_Document? document = OnPingDocument();

        switch (action.ToLowerInvariant())
        {
            case "set":
                if (string.IsNullOrWhiteSpace(key))
                {
                    return ToolCallResult.Error("\"set\" needs a key.");
                }

                string? refusal = StateStore.Set(document, key, value);
                return refusal is null
                    ? ToolCallResult.Ok($"Recorded {key.Trim()} = {Describe(value)}. The definition can read it now.")
                    : ToolCallResult.Error(refusal);

            case "get":
                if (string.IsNullOrWhiteSpace(key))
                {
                    return ToolCallResult.Error("\"get\" needs a key. Use \"list\" to see what is set.");
                }

                string? stored = StateStore.Get(document, key);
                return stored is null
                    ? ToolCallResult.Ok($"{key.Trim()} is not set.")
                    : ToolCallResult.Ok($"{key.Trim()} = {stored}");

            case "list":
                return ToolCallResult.Ok(Render(StateStore.All(document)));

            case "clear":
                int removed = StateStore.Clear(document, key);
                return ToolCallResult.Ok(string.IsNullOrWhiteSpace(key)
                    ? $"Cleared the whole board ({removed} value{(removed == 1 ? string.Empty : "s")})."
                    : removed > 0
                        ? $"Cleared {key.Trim()}."
                        : $"{key.Trim()} was not set, so there was nothing to clear.");

            default:
                // Named back exactly as offered, so a model that guessed has something to correct
                // towards rather than only being told it was wrong.
                return ToolCallResult.Error(
                    $"\"{action}\" is not an action. Use one of: set, get, list, clear.");
        }
    }

    private static string Render(IReadOnlyList<KeyValuePair<string, string>> board)
    {
        if (board.Count == 0)
        {
            return "Nothing is on this pipeline's state board yet.";
        }

        var text = new StringBuilder("This pipeline's state, most recently set last:");

        foreach (KeyValuePair<string, string> pair in board)
        {
            text.Append("\n  ").Append(pair.Key).Append(" = ").Append(pair.Value);
        }

        return text.ToString();
    }

    private static string Describe(string value) =>
        string.IsNullOrEmpty(value) ? "(empty)" : value.Length > 80 ? value.Substring(0, 80) + "…" : value;

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
}
