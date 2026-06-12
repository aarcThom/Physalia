// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Models.Protocol;

namespace Physalia.GH.Components;

/// <summary>
/// Adjusts the inference parameters of any OpenAI-compatible model configuration.
/// </summary>
public class OpenAICompatibleTweaker : TweakerComponentBase<OpenAIProtocolConfig>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAICompatibleTweaker"/> class.
    /// </summary>
    public OpenAICompatibleTweaker()
        : base(
            "OpenAI Compatible Tweaker",
            "OAITwk",
            "Adjusts temperature, top-p, and max tokens on any OpenAI-compatible model configuration.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D5E6F7A8-B9C0-4D1E-F2A3-B4C5D6E7F8A9");

    /// <inheritdoc/>
    protected override string ModelInputDescription => "OpenAI-compatible model configuration to adjust.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription => "Adjusted OpenAI-compatible model configuration.";

    /// <inheritdoc/>
    protected override string TemperatureDescription => "Sampling temperature (0.0–2.0).";

    /// <inheritdoc/>
    protected override double TopPDefault => 1.0;

    /// <inheritdoc/>
    protected override int ThirdParamDefault => 4096;

    /// <inheritdoc/>
    protected override string WrongConfigTypeMessage => "Input model must be an OpenAI-compatible configuration.";

    /// <inheritdoc/>
    protected override void RegisterThirdParam(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Max Tokens", "N", "Maximum number of tokens to generate.", GH_ParamAccess.item, 4096);
    }

    /// <inheritdoc/>
    protected override OpenAIProtocolConfig Adjust(OpenAIProtocolConfig existing, float temperature, float topP, int thirdValue)
        => existing with
        {
            Temperature = temperature,
            TopP = topP,
            MaxTokens = thirdValue,
        };
}
