// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tools;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Mints a signal that calls a tool node directly, without the model.
/// </summary>
/// <remarks>
/// <para>Wire the Signal output into any LLM Tool node's Signal input and the tool runs exactly as
/// it does for the model — same arguments, same work, same outputs on the canvas. What it does NOT
/// do is answer the model: a call made here carries a manual id, and a tool node given one publishes
/// its data and stays silent on its Result output. A tool result has to echo an id the assistant
/// actually asked with, and there is no such turn here; sending one anyway is what a provider
/// rejects the whole request over.</para>
/// <para>So this is how a pipeline uses a tool as a tool — a script composing a query, a report
/// firing a snapshot — rather than a way to fake a conversation turn.</para>
/// </remarks>
public class ConstructToolCall : StatefulComponentBase
{
    private const int InToolName = 0;
    private const int InArguments = 1;
    private const int InTrigger = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConstructToolCall"/> class.
    /// </summary>
    public ConstructToolCall()
        : base(
            "Construct Tool Call",
            "ConCall",
            "Calls a tool node yourself, one press at a time, without going through the model. The tool does its work and fills its own outputs; nothing is said to the model about it.",
            "Signals")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B4E09D71-83A5-4C62-A0F7-52D6C1E8B93A");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Tool Name",
            "T",
            "The name of the tool being called, as the node advertises it — an API Call node named for the 'vancouver' API answers to 'api__vancouver'. A tool node given a name it does not recognise says so.",
            GH_ParamAccess.item,
            string.Empty);

        pManager.AddTextParameter(
            "Arguments",
            "A",
            "The call arguments as a JSON object, exactly as the model would have written them — for example {\"path\": \"catalog/datasets\", \"query\": \"limit=5\"}.",
            GH_ParamAccess.item,
            "{}");

        pManager.AddBooleanParameter(
            "Trigger",
            "Tr",
            "One press, one call. Wire a Button here. Opening or pasting the file fires nothing — only a real press does.",
            GH_ParamAccess.item,
            false);

        pManager[InToolName].Optional = true;
        pManager[InArguments].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_Signal(),
            "Signal",
            "S",
            "The call, held on the wire until the next press. Wire it into a tool node's Signal input.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // A source, not a processing hop: the latch happens on the press solve, exactly as
        // Construct Signal does.
        if (ObserveButtonPress(DA, InTrigger))
        {
            string toolName = string.Empty;
            string arguments = "{}";
            DA.GetData(InToolName, ref toolName);
            DA.GetData(InArguments, ref arguments);

            if (string.IsNullOrWhiteSpace(toolName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No tool name, so there is nothing to call.");
            }
            else
            {
                var call = new ToolCallContent(
                    ManualToolCall.NewId(),
                    toolName.Trim(),
                    string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);

                LatchSuccess(
                    $"Manual call to {toolName.Trim()}",
                    emitSignal: true,
                    contentBlocks: new List<MessageContent> { call });
            }
        }

        EmitSignal(DA, 0, SuccessSignal);
    }
}
