// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Models.Named;

namespace Physalia.GH.Components;

/// <summary>
/// Adjusts the inference parameters of an Anthropic model configuration.
/// </summary>
public class AnthropicTweaker : TweakerComponentBase<AnthropicConfig>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnthropicTweaker"/> class.
    /// </summary>
    public AnthropicTweaker()
        : base("Anthropic Tweaker", "AnthTwk", "Changes how an Anthropic model picks its words, and how much it is allowed to think before answering.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("ED550693-9492-482E-A70F-9BAD732B3C4F");

    /// <inheritdoc/>
    protected override string ModelInputDescription =>
        "The Anthropic model to adjust. Wire an Anthropic Model component.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription =>
        "The same Anthropic model with these settings applied. Wire into an LLM Call.";

    /// <inheritdoc/>
    protected override string TemperatureDescription =>
        "How freely it words things, from 0 to 1. Anything outside that range is pulled back in, because Anthropic will not accept it.";

    /// <inheritdoc/>
    protected override string TopPDescription =>
        "Narrows the choice to the likeliest words only. 1 considers them all, which is Anthropic's own default.";

    /// <inheritdoc/>
    protected override double TopPDefault => 1.0;

    /// <inheritdoc/>
    protected override int ThirdParamDefault => 0;

    /// <inheritdoc/>
    protected override string WrongConfigTypeMessage => "Input model must be an Anthropic configuration.";

    /// <inheritdoc/>
    protected override void RegisterThirdParam(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Top K", "K", "How many candidate words are in play at each step. 0 leaves it to Anthropic.", GH_ParamAccess.item, 0);
    }

    /// <inheritdoc/>
    protected override AnthropicConfig Adjust(AnthropicConfig existing, float temperature, float topP, int thirdValue)
        => existing with
        {
            Temperature = temperature,
            TopP = topP,
            TopK = thirdValue,
        };

    /// <inheritdoc/>
    protected override void RegisterAdditionalParams(GH_InputParamManager pManager)
    {
        int index = pManager.AddIntegerParameter(
            "Thinking Budget",
            "TB",
            "How much thinking to allow. Left unwired, the model does whatever it normally does — the ones that think by default keep doing so, with a readable summary. 0 turns thinking off; -1 lets the model decide how long to think; a positive number is a token budget (nudged up to Anthropic's minimum of 1024, with Max Tokens raised to fit). While thinking is on, Temperature, Top P and Top K are ignored.",
            GH_ParamAccess.item);
        pManager[index].Optional = true;
    }

    /// <inheritdoc/>
    protected override AnthropicConfig AdjustAdditional(AnthropicConfig config, IGH_DataAccess da)
    {
        int budget = 0;
        return da.GetData(4, ref budget) ? config with { ThinkingBudget = budget } : config;
    }
}
