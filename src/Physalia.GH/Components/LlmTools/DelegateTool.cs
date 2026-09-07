// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Hands a task to another harness and waits for its answer, as a tool the model can call. The
/// scoping mechanism the plug-in did not have.
///
/// <para><b>What was missing.</b> A pipeline has exactly ONE Conversation Log, so every subtask ever
/// asked lands in the same context and stays there: a twelve-room survey is twelve tasks in one
/// conversation, a classification and a generation share a model because they share a wire, and a
/// long piece of side work poisons the thread it was done on. Compaction can shrink that history but
/// cannot separate it. This separates it — the inner harness has its own Conversation Log, its own
/// model, its own tools and its own guardrails, and only the ANSWER comes back.</para>
///
/// <para><b>Why a whole harness rather than a hidden sub-agent.</b> Because the harness is already
/// the plug-in's unit of a pipeline, and because a black box would be the one part of Physalia the
/// user could not see, edit, validate or ship. A delegate points at a real harness sitting on the
/// same canvas: open it, watch it work, put a Geometry Report in it, save it as a preset. Everything
/// that works in a pipeline works in a delegated one, including another delegate.</para>
///
/// <para><b>The description is not optional, and neither is the link.</b> A model choosing whether to
/// delegate needs to know what it is delegating TO; without that the tool is a mystery it will
/// either ignore or misuse. So an unlinked or undescribed node advertises NOTHING at all — the same
/// rule as an API Call node with no endpoint picked, for the same reason: a tool that fails every
/// call reads to the model as a broken tool rather than an unconfigured node.</para>
///
/// <para><b>One task at a time.</b> A harness has one conversation, so two tasks running through it
/// would interleave into it and neither answer would be trustworthy. A second call is refused with
/// that reason, which is also what stops most delegation cycles — a harness cannot be asked to
/// delegate to one that is already working.</para>
/// </summary>
public class DelegateTool : LlmToolComponentBase, IGuidLinked
{
    private const int InName = 1;
    private const int InDescription = 2;
    private const int InTimeout = 3;

    private static readonly int OutLastTask = FirstAdditionalOutputIndex;
    private static readonly int OutLastAnswer = FirstAdditionalOutputIndex + 1;

    private Guid _linkedGuid = Guid.Empty;

    private string _toolName = string.Empty;

    private string _description = string.Empty;

    private double _timeoutSeconds = 300.0;

    private string _lastTask = string.Empty;

    private string _lastAnswer = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateTool"/> class.
    /// </summary>
    public DelegateTool()
        : base(
            "Delegate",
            "Delegate",
            "Lets the model hand a task to another harness and wait for its answer. Drag the grip onto a harness to link it. The sub-pipeline has its own conversation, so side work does not pile up in this one.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("5C81A62F-D374-4B09-96E2-4A70F18BD53C");

    /// <summary>
    /// Gets the instance id of the harness this tool calls, or <see cref="Guid.Empty"/> when
    /// unlinked. Read by <see cref="Attributes.DelegateAttrib"/> to draw the wire.
    /// </summary>
    public Guid LinkedGuid => _linkedGuid;

    /// <inheritdoc/>
    /// <remarks>
    /// Empty until the node is both linked and described, so the model is never offered a delegate it
    /// cannot use or cannot understand. See the class remarks.
    /// </remarks>
    protected override IReadOnlyList<LlmToolDefinition> Definitions =>
        _linkedGuid != Guid.Empty && !string.IsNullOrWhiteSpace(_description)
            ? new[] { Definition }
            : Array.Empty<LlmToolDefinition>();

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => new(
        ToolName(),
        _description.Trim()
        + "\n\nHand over ONE self-contained task and wait: the pipeline you are calling starts from "
        + "nothing and cannot see this conversation, so say everything it needs in the task itself. "
        + "What comes back is its answer, and any images it produced.",
        "{\"type\":\"object\",\"properties\":"
        + "{\"task\":{\"type\":\"string\",\"description\":\"The task, stated in full. The pipeline you are calling has no access to this conversation, so include every fact, constraint and name it needs.\"}},"
        + "\"required\":[\"task\"]}");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "A task the model wants delegated, sent here by the Router.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises the linked harness to the model as something it can hand work to. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire. Nothing is advertised until the node is linked and described.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "The sub-pipeline's answer heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    /// <remarks>
    /// A whole pipeline runs behind this — its own inference calls, its own tool rounds — so it is a
    /// wait measured in minutes. Holding a solution open for that is not an option.
    /// </remarks>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new Attributes.DelegateAttrib(this);
    }

    /// <summary>
    /// Links this tool to a harness. Called by <see cref="Attributes.DelegateAttrib"/> on a drop.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the harness to call.</param>
    public void LinkTo(Guid guid)
    {
        _linkedGuid = guid;
    }

    /// <summary>
    /// Removes the current link, so this node advertises nothing. Called by
    /// <see cref="Attributes.DelegateAttrib"/> on a Ctrl+drop.
    /// </summary>
    public void Unlink()
    {
        _linkedGuid = Guid.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The harness is a peer in the same document, so the link is re-pointed when a document's ids are
    /// re-issued — which is what lets a preset ship with its delegation already wired up.
    /// </remarks>
    void IGuidLinked.RemapLinks(IReadOnlyDictionary<Guid, Guid> replacements)
    {
        if (replacements.TryGetValue(_linkedGuid, out Guid replacement))
        {
            _linkedGuid = replacement;
        }
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Tool Name",
            "N",
            "What the model calls this delegate — \"assess_structure\", \"price_facade\". Blank uses this node's nickname. Two delegates must not end up with the same name; the Router dispatches on it.",
            GH_ParamAccess.item,
            string.Empty);
        pManager.AddTextParameter(
            "Description",
            "D",
            "What the linked pipeline is FOR, and when to hand it work. Required — nothing is advertised without it, because a model cannot sensibly delegate to something it has not been told about.",
            GH_ParamAccess.item,
            string.Empty);
        pManager.AddNumberParameter(
            "Timeout",
            "T",
            "Seconds to wait for the sub-pipeline to answer. A whole pipeline runs behind this, tool rounds included, so give it room.",
            GH_ParamAccess.item,
            300.0);
        pManager[InName].Optional = true;
        pManager[InDescription].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Last Task",
            "LT",
            "The task last handed over, so what was delegated can be read on the canvas.",
            GH_ParamAccess.item);
        pManager.AddTextParameter(
            "Last Answer",
            "LA",
            "What came back from it.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        string name = string.Empty;
        string description = string.Empty;
        double timeout = 300.0;
        da.GetData(InName, ref name);
        da.GetData(InDescription, ref description);
        da.GetData(InTimeout, ref timeout);

        _toolName = name ?? string.Empty;
        _description = description ?? string.Empty;
        _timeoutSeconds = Math.Max(1.0, timeout);

        if (_linkedGuid == Guid.Empty)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Not linked to a harness, so nothing is advertised to the model. Drag the grip at the bottom of this node onto the harness it should call.");
        }
        else if (Target() is null)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "The linked harness is no longer in this document. Drag the grip onto another one.");
        }
        else if (string.IsNullOrWhiteSpace(_description))
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "No Description, so nothing is advertised to the model. Say what the linked pipeline is for — that is what the model reads to decide whether to hand it work.");
        }

        Message = DescribeState();
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        // From OnSolveEnd, not OnSolveTick: the call sets these and OnSolveTick runs before it.
        da.SetData(OutLastTask, _lastTask);
        da.SetData(OutLastAnswer, _lastAnswer);
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        string task;

        try
        {
            using JsonDocument args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.InputJson) ? "{}" : call.InputJson);
            task = args.RootElement.TryGetProperty("task", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException ex)
        {
            return ToolCallResult.Error($"The arguments were not valid JSON: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            return ToolCallResult.Error("A delegated task must not be empty. Say what is to be done, in full.");
        }

        HarnessComponent? target = Target();
        if (target is null)
        {
            return ToolCallResult.Error(
                "This delegate is not linked to a harness that exists, so there is nobody to hand the task to. Tell the user.");
        }

        GH_Document? inner = target.InnerDocument;

        // Self-reference: a harness cannot be its own sub-pipeline. Caught here rather than left to
        // the one-task-at-a-time rule, because this one is a wiring mistake with a clear message
        // available, and the other would report it as a busy harness.
        if (inner is not null && ReferenceEquals(inner, OnPingDocument()))
        {
            return ToolCallResult.Error(
                "This delegate is linked to the harness it lives in, which would call itself. Tell the user to link it to a different harness.");
        }

        _lastTask = task;

        DelegationResult result = await DelegationBroker
            .RunAsync(inner, task, TimeSpan.FromSeconds(_timeoutSeconds), ct)
            .ConfigureAwait(false);

        _lastAnswer = result.Text;

        if (!result.Completed)
        {
            return ToolCallResult.Error(result.Text);
        }

        var body = new StringBuilder();
        body.Append(string.IsNullOrWhiteSpace(result.Text)
            ? "The delegated pipeline finished but returned no text."
            : result.Text);

        // Anything that is not text — an image the sub-pipeline produced — rides back as an
        // attachment on the answering turn, which is the machinery a tool already has for it.
        List<MessageContent> attachments = result.Blocks
            .Where(b => b is not TextContent)
            .ToList();

        if (attachments.Count > 0)
        {
            body.Append("\n\nIt also returned ")
                .Append(attachments.Count == 1 ? "an attachment" : $"{attachments.Count} attachments")
                .Append(", included with this result.");

            return ToolCallResult.OkWith(body.ToString(), attachments);
        }

        return ToolCallResult.Ok(body.ToString());
    }

    /// <inheritdoc/>
    protected override void ClearStateOutputs()
    {
        _lastTask = string.Empty;
        _lastAnswer = string.Empty;
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetGuid("LinkedHarness", _linkedGuid);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("LinkedHarness"))
        {
            _linkedGuid = reader.GetGuid("LinkedHarness");
        }

        return base.Read(reader);
    }

    // Sanitized to the provider tool-name charset, and namespaced, for the reason MCP namespaces its
    // server's tools: two delegates in one pipeline are the normal case, and the Router dispatches on
    // the name, so a collision would send one pipeline's work to the other.
    private string ToolName()
    {
        string raw = string.IsNullOrWhiteSpace(_toolName) ? NickName : _toolName;
        var clean = new StringBuilder("delegate__");

        foreach (char c in raw.Trim())
        {
            clean.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? char.ToLowerInvariant(c) : '_');
        }

        string name = clean.ToString();
        return name.Length > 64 ? name.Substring(0, 64) : name;
    }

    private HarnessComponent? Target()
    {
        if (_linkedGuid == Guid.Empty)
        {
            return null;
        }

        // The local document, not the host: the harness being called is a peer in this pipeline —
        // a harness inside a harness — which is what lets the link be made by a drag at all.
        return OnPingDocument()?.FindObject(_linkedGuid, false) as HarnessComponent;
    }

    private string DescribeState()
    {
        if (_linkedGuid == Guid.Empty)
        {
            return "unlinked";
        }

        HarnessComponent? target = Target();
        if (target is null)
        {
            return "link broken";
        }

        string? running = DelegationBroker.RunningTask(target.InnerDocument);
        return running is null ? target.NickName : $"running: {target.NickName}";
    }
}
