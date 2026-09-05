// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.Core.Models;
using Physalia.Core.Models.Named;
namespace Physalia.GH.Components;

/// <summary>
/// Grasshopper component that configures an Anthropic model.
/// Fetches available models from the API and exposes them via <see cref="IPickableValuesSource"/>.
/// </summary>
public class AnthropicModel : ModelComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnthropicModel"/> class.
    /// </summary>
    public AnthropicModel()
        : base("Anthropic Model", "Anth", "Points the pipeline at an Anthropic model. The list of models on offer is fetched from the API as soon as a key arrives.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D60822A6-1ABD-4BA8-AB0F-A54937D0B923");

    /// <inheritdoc/>
    protected override string ModelApiDescription =>
        "Your Anthropic endpoint and key. Wire a Model API component; the model list is fetched the moment it arrives.";

    /// <inheritdoc/>
    protected override string ModelIdDescription =>
        "Which Claude to use, e.g. claude-sonnet-4-6. The Picker placed alongside fills with whatever the key can reach.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription =>
        "The Anthropic model, configured. Wire into an LLM Call, or through an Anthropic Tweaker first to change how it samples.";

    /// <inheritdoc/>
    protected override ModelConfig CreateConfig(string modelId, ModelApi api)
        => new AnthropicConfig(
            ModelId: modelId,
            ApiKey: api.Key,
            BaseUrl: api.BaseUrlOr("https://api.anthropic.com/v1"));

    /// <inheritdoc/>
    /// <remarks>
    /// Thinking and answer share this budget, so the default is a generous 32768 — 8192
    /// truncated real responses mid-document once adaptive thinking ran.
    /// </remarks>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Max Tokens", "T", "The ceiling on one reply, thinking included. Anthropic insists on a number here, so there is no leaving it out.", GH_ParamAccess.item, 32768);
    }

    /// <inheritdoc/>
    protected override ModelConfig ApplyAdditionalInputs(ModelConfig config, IGH_DataAccess da)
    {
        int maxTokens = 32768;
        da.GetData(2, ref maxTokens);
        return config is AnthropicConfig anthropic ? anthropic with { MaxTokens = maxTokens } : config;
    }
}
