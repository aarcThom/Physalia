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
/// Lets the model say where the work stands, and turns that into a signal. The model as the branch
/// rather than the graph guessing at one.
///
/// <para><b>Why a declared route beats reading the prose.</b> Everything that decides what happens
/// next in Physalia currently infers it: Detect JSON asks whether the reply looked like JSON, the
/// Geometry Report asks the model to reply in prose if it is satisfied, a Signal Switch can be pointed
/// at the word "done" — and that last one finds it in "not done yet" too. An inference about intent is
/// wrong at exactly the moments that matter, because a model hedges most when it is least sure. This
/// asks instead. The model picks one of the routes the pipeline offers, and picking is unambiguous.</para>
///
/// <para><b>The routes are the pipeline's vocabulary, not the tool's.</b> They are typed on the node,
/// so they ship in the .gh and inside a preset — the same reasoning as the Memory tool's folder name
/// and the API node's catalog. They also go into the schema as an enum, so what the model is offered
/// and what the node accepts cannot drift apart; a route that is not on the list comes back as an
/// error listing the ones that are, rather than being quietly accepted.</para>
///
/// <para><b>Branching on it.</b> Wire <b>Route</b> into an equality test and that into a Signal Gate's
/// Open input: exact, one gate per route you care about. A Signal Switch on the payload works too and
/// is looser, since the payload carries the model's own note.</para>
///
/// <para><b>When the signal fires, and what that means for a Conversation Log.</b> Immediately — in
/// the same solve as the tool result, because the tool result is the only moment this node is
/// awake and there is no later point at which "the round finished" is observable from inside a tool.
/// So a declaration wired into a Conversation Log's Prompt Signal joins the same turn as the tool
/// result, which the Conversation Log already handles (a second user-side event before an assistant
/// turn merges into the one turn). The result text tells the model the pipeline has been notified and
/// to stop, since it does get one more turn.</para>
/// </summary>
public class DeclareTool : LlmToolComponentBase
{
    private const int InRoutes = 1;
    private const int InInstruction = 2;

    private static readonly int OutDeclared = FirstAdditionalOutputIndex;
    private static readonly int OutRoute = FirstAdditionalOutputIndex + 1;
    private static readonly int OutNote = FirstAdditionalOutputIndex + 2;

    /// <summary>
    /// The routes a fresh node offers. Three, because they are the three ways a stage of work actually
    /// ends: it worked, it needs a person, or it cannot be done. Anything more specific belongs to the
    /// pipeline that needs it and is typed in.
    /// </summary>
    private static readonly string[] DefaultRoutes = { "done", "needs_input", "failed" };

    private List<string> _routes = DefaultRoutes.ToList();

    private string _instruction = string.Empty;

    private string _route = string.Empty;

    private string _note = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeclareTool"/> class.
    /// </summary>
    public DeclareTool()
        : base(
            "Declare",
            "Declare",
            "Lets the model say where the work stands — finished, needs the human, failed — and turns that into a signal. Wire Route into a comparison and a Signal Gate to act on each one.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("1D74E9C3-58B0-42A6-9F31-6C08B7A5D2E4");

    /// <inheritdoc/>
    /// <remarks>
    /// One of only two tools that override this, and for the documented reason: leaving the decision to
    /// the model is itself the failure here. A model that is not told it must declare simply answers in
    /// prose — which is what the whole node exists to stop the pipeline having to interpret. The tool
    /// description would be read only once the model was already considering the call, and it has no
    /// reason to consider it.
    /// </remarks>
    public override string? GroundingDirective
    {
        get
        {
            var text = new StringBuilder();
            text.Append("When you have finished a stage of work, need something from the human, or cannot continue, you MUST call the `declare` tool with the route that matches (")
                .Append(string.Join(", ", _routes.Select(r => $"`{r}`")))
                .Append("). The pipeline acts on that declaration and does nothing until it arrives, so answering in prose alone leaves the work stopped.");

            if (!string.IsNullOrWhiteSpace(_instruction))
            {
                text.Append(' ').Append(_instruction.Trim());
            }

            return text.ToString();
        }
    }

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "A declaration the model has made, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises the declaration to the model: one of the routes, plus a note. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "The acknowledgement heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => new(
        "declare",
        "Declare where the work stands so the pipeline can act on it: which route applies, and a short note saying why. Call this the moment a stage is finished, you need something only the human can give you, or you cannot continue — the pipeline is waiting on it and does nothing until it arrives.",
        BuildSchema(_routes));

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Routes",
            "R",
            "The routes this pipeline offers the model, one per item. Whatever you put here is what the model is allowed to pick, and it comes out on Route. Leave unwired for done / needs_input / failed.",
            GH_ParamAccess.list);
        pManager.AddTextParameter(
            "Instruction",
            "I",
            "Anything extra the model should know about when to declare — added to the standing instruction that already tells it declaring is required.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[InRoutes].Optional = true;
        pManager[InInstruction].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(
            new Parameters.Param_Signal(),
            "Declared",
            "D",
            "Fires when the model declares, carrying its note. Wire into whatever should happen next — a Conversation Log's Prompt Signal for a standing follow-up prompt, a transmitter, a Signal Gate.",
            GH_ParamAccess.item);
        pManager.AddTextParameter(
            "Route",
            "R",
            "Which route the model picked. Wire into an equality test and that into a Signal Gate's Open input — that is the exact way to branch on this.",
            GH_ParamAccess.item);
        pManager.AddTextParameter(
            "Note",
            "N",
            "What the model said about it, on its own so it can be shown or logged without the signal.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        var routes = new List<string>();
        da.GetDataList(InRoutes, routes);

        string instruction = string.Empty;
        da.GetData(InInstruction, ref instruction);
        _instruction = instruction ?? string.Empty;

        List<string> cleaned = routes
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Unwired means the defaults, not "no routes": a node offering nothing would advertise a tool
        // the model can never call successfully, which reads to it as a broken tool rather than an
        // unconfigured node — the same reasoning as an API Call node with no endpoint picked
        // advertising nothing at all.
        _routes = cleaned.Count > 0 ? cleaned : DefaultRoutes.ToList();

        if (cleaned.Count == 1)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Remark,
                "One route means the model has nothing to choose between — this is then a plain \"I have finished\" trigger, which is a fine thing to want.");
        }
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        // Published from here, not OnSolveTick: the call itself sets these, and OnSolveTick runs before
        // it. Re-published unconditionally so the values stay on the wire between solves.
        EmitSignal(da, OutDeclared, SuccessSignal);
        da.SetData(OutRoute, _route);
        da.SetData(OutNote, _note);
    }

    /// <inheritdoc/>
    protected override ToolCallResult ExecuteCall(ToolCallContent call)
    {
        string route;
        string note;

        try
        {
            using JsonDocument args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.InputJson) ? "{}" : call.InputJson);
            route = args.RootElement.TryGetProperty("route", out JsonElement r) ? r.GetString() ?? string.Empty : string.Empty;
            note = args.RootElement.TryGetProperty("note", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException ex)
        {
            return ToolCallResult.Error($"The arguments were not valid JSON: {ex.Message}");
        }

        string? matched = _routes.FirstOrDefault(r => string.Equals(r, route, StringComparison.OrdinalIgnoreCase));

        if (matched is null)
        {
            // Named back exactly as offered, the way Move In Space re-lists its legal moves: a model
            // told only that it was wrong has nothing to correct towards.
            return ToolCallResult.Error(
                $"\"{route}\" is not one of this pipeline's routes. Pick one of: {string.Join(", ", _routes)}.");
        }

        _route = matched;
        _note = note ?? string.Empty;

        string payload = string.IsNullOrWhiteSpace(_note)
            ? $"The model declared \"{matched}\"."
            : _note;

        // The base never touches SuccessSignal — it mints its result signal into a field of its own —
        // so the ordinary latch is free for the declaration, and it brings the caption with it.
        LatchSuccess(payload);

        return ToolCallResult.Ok(
            $"Declared \"{matched}\". The pipeline has been notified and is acting on it. "
            + "Do not carry on with this line of work unless the next turn asks you to.");
    }

    /// <inheritdoc/>
    protected override void ClearStateOutputs()
    {
        _route = string.Empty;
        _note = string.Empty;
    }

    private static string BuildSchema(IReadOnlyList<string> routes)
    {
        // Generated from the same list the node validates against, so what the model is offered and
        // what is accepted are one fact. The SpaceNavigator token enum is built this way for the same
        // reason.
        string enumJson = string.Join(",", routes.Select(r => JsonSerializer.Serialize(r)));

        return "{\"type\":\"object\",\"properties\":{"
            + "\"route\":{\"type\":\"string\",\"enum\":[" + enumJson + "],\"description\":\"Where the work stands.\"},"
            + "\"note\":{\"type\":\"string\",\"description\":\"One or two sentences saying why — what was finished, what is needed, or what stopped you.\"}"
            + "},\"required\":[\"route\"]}";
    }
}
