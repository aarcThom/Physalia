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
        : base("Gemini Tweaker", "GemTwk", "Adjusts temperature, top-p, and top-k on a Gemini model configuration.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("DE6C8CD4-79C8-458F-9BE8-D68CF88DC1A8");

    /// <inheritdoc/>
    protected override string ModelInputDescription => "Gemini model configuration to adjust.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription => "Adjusted Gemini model configuration.";

    /// <inheritdoc/>
    protected override string TemperatureDescription => "Sampling temperature (0.0–2.0).";

    /// <inheritdoc/>
    protected override double TopPDefault => 0.95;

    /// <inheritdoc/>
    protected override int ThirdParamDefault => 0;

    /// <inheritdoc/>
    protected override string WrongConfigTypeMessage => "Input model must be a Gemini configuration.";

    /// <inheritdoc/>
    protected override void RegisterThirdParam(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Top K", "K", "Top-K sampling pool size. Set to 0 to use the provider default.", GH_ParamAccess.item, 0);
    }

    /// <inheritdoc/>
    protected override GeminiConfig Adjust(GeminiConfig existing, float temperature, float topP, int thirdValue)
        => existing with
        {
            Temperature = temperature,
            TopP = topP,
            TopK = thirdValue,
        };
}
