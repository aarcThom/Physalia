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
            "Changes how an OpenAI-compatible model picks its words, how long a reply may run, and how hard a reasoning model thinks.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D5E6F7A8-B9C0-4D1E-F2A3-B4C5D6E7F8A9");

    /// <inheritdoc/>
    protected override string ModelInputDescription =>
        "The model to adjust. Wire an OpenAI Compatible Model component.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription =>
        "The same model with these settings applied. Wire into an LLM Call.";

    /// <inheritdoc/>
    protected override string TemperatureDescription =>
        "How freely it words things, from 0 to 2. Reasoning models refuse this outright, and it is left out of the request for them.";

    /// <inheritdoc/>
    protected override string TopPDescription =>
        "Narrows the choice to the likeliest words only. 1 considers them all.";

    /// <inheritdoc/>
    protected override double TopPDefault => 1.0;

    /// <inheritdoc/>
    protected override int ThirdParamDefault => 4096;

    /// <inheritdoc/>
    protected override string WrongConfigTypeMessage => "Input model must be an OpenAI-compatible configuration.";

    /// <inheritdoc/>
    protected override void RegisterThirdParam(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Max Tokens", "N", "The ceiling on one reply, replacing whatever the Model component set.", GH_ParamAccess.item, 4096);
    }

    /// <inheritdoc/>
    protected override OpenAIProtocolConfig Adjust(OpenAIProtocolConfig existing, float temperature, float topP, int thirdValue)
        => existing with
        {
            Temperature = temperature,
            TopP = topP,
            MaxTokens = thirdValue,
        };

    /// <inheritdoc/>
    protected override void RegisterAdditionalParams(GH_InputParamManager pManager)
    {
        int effortIndex = pManager.AddTextParameter(
            "Reasoning Effort",
            "E",
            "How hard a reasoning model should think: low, medium or high. Leave it unwired for models and servers that have no such setting.",
            GH_ParamAccess.item);
        pManager[effortIndex].Optional = true;

        int thinkingIndex = pManager.AddBooleanParameter(
            "Thinking",
            "TH",
            "Switches thinking on for models that have to be asked — DeepSeek will not show its reasoning otherwise. Left unwired, each model does whatever it normally does; wire true or false to insist.",
            GH_ParamAccess.item);
        pManager[thinkingIndex].Optional = true;
    }

    /// <inheritdoc/>
    protected override OpenAIProtocolConfig AdjustAdditional(OpenAIProtocolConfig config, IGH_DataAccess da)
    {
        string? effort = null;
        if (da.GetData(4, ref effort) && !string.IsNullOrWhiteSpace(effort))
        {
            config = config with { ReasoningEffort = effort.Trim().ToLowerInvariant() };
        }

        // Wired true/false is an explicit override; unwired leaves null = model default.
        bool thinking = false;
        if (da.GetData(5, ref thinking))
        {
            config = config with { ThinkingEnabled = thinking };
        }

        return config;
    }
}
