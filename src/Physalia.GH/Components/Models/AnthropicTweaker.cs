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
        : base("Anthropic Tweaker", "AnthTwk", "Adjusts temperature, top-p, and top-k on an Anthropic model configuration.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("ED550693-9492-482E-A70F-9BAD732B3C4F");

    /// <inheritdoc/>
    protected override string ModelInputDescription => "Anthropic model configuration to adjust.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription => "Adjusted Anthropic model configuration.";

    /// <inheritdoc/>
    protected override string TemperatureDescription => "Sampling temperature (0.0–1.0). Clamped on intake.";

    /// <inheritdoc/>
    protected override double TopPDefault => 1.0;

    /// <inheritdoc/>
    protected override int ThirdParamDefault => 0;

    /// <inheritdoc/>
    protected override string WrongConfigTypeMessage => "Input model must be an Anthropic configuration.";

    /// <inheritdoc/>
    protected override void RegisterThirdParam(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Top K", "K", "Top-K sampling pool size. Set to 0 to use the provider default.", GH_ParamAccess.item, 0);
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
            "Extended thinking control. Unwired applies the model's known default behaviour (models that think by default get readable summaries automatically); 0 explicitly disables thinking; -1 requests adaptive thinking with readable summaries; positive values set a manual budget (raised to the 1024 API minimum; Max Tokens auto-bumped). Requests are mapped to the form the model accepts. Temperature/Top P/Top K are ignored while thinking is enabled.",
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
