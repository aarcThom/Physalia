// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Models.Named;

namespace Physalia.GH.Components;

/// <summary>
/// Adjusts the inference parameters of a Gemini model configuration.
/// </summary>
public class GeminiTweaker : TweakerComponentBase<GeminiConfig>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiTweaker"/> class.
    /// </summary>
    public GeminiTweaker()
        : base("Gemini Tweaker", "GemTwk", "Changes how a Gemini model picks its words, and how much it is allowed to think before answering.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("DE6C8CD4-79C8-458F-9BE8-D68CF88DC1A8");

    /// <inheritdoc/>
    protected override string ModelInputDescription =>
        "The Gemini model to adjust. Wire a Gemini Model component.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription =>
        "The same Gemini model with these settings applied. Wire into an LLM Call.";

    /// <inheritdoc/>
    protected override string TemperatureDescription =>
        "How freely it words things, from 0 to 2.";

    /// <inheritdoc/>
    protected override string TopPDescription =>
        "Narrows the choice to the likeliest words only. Gemini's own default is 0.95.";

    /// <inheritdoc/>
    protected override double TopPDefault => 0.95;

    /// <inheritdoc/>
    protected override int ThirdParamDefault => 0;

    /// <inheritdoc/>
    protected override string WrongConfigTypeMessage => "Input model must be a Gemini configuration.";

    /// <inheritdoc/>
    protected override void RegisterThirdParam(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Top K", "K", "How many candidate words are in play at each step. 0 leaves it to Gemini.", GH_ParamAccess.item, 0);
    }

    /// <inheritdoc/>
    protected override GeminiConfig Adjust(GeminiConfig existing, float temperature, float topP, int thirdValue)
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
            "How much thinking to allow. Left unwired, the model does whatever it normally does. 0 turns thinking off where the model permits it; -1 lets the model decide how long to think; a positive number caps the thinking tokens.",
            GH_ParamAccess.item);
        pManager[index].Optional = true;
    }

    /// <inheritdoc/>
    protected override GeminiConfig AdjustAdditional(GeminiConfig config, IGH_DataAccess da)
    {
        int budget = 0;
        return da.GetData(4, ref budget) ? config with { ThinkingBudget = budget } : config;
    }
}
