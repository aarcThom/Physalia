// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
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
        : base("Anthropic Model", "Anth", "Configures an Anthropic model and fetches available models from the API.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D60822A6-1ABD-4BA8-AB0F-A54937D0B923");

    /// <inheritdoc/>
    protected override string ApiKeyDescription => "Anthropic API key.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription => "Configured Anthropic model.";

    /// <inheritdoc/>
    protected override ModelConfig CreateConfig(string modelId, string apiKey)
        => new AnthropicConfig(ModelId: modelId, ApiKey: apiKey);

    /// <inheritdoc/>
    /// <remarks>
    /// Thinking and answer share this budget, so the default is a generous 32768 — 8192
    /// truncated real responses mid-document once adaptive thinking ran.
    /// </remarks>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Max Tokens", "T", "Maximum number of tokens to generate (thinking + answer).", GH_ParamAccess.item, 32768);
    }

    /// <inheritdoc/>
    protected override ModelConfig ApplyAdditionalInputs(ModelConfig config, IGH_DataAccess da)
    {
        int maxTokens = 32768;
        da.GetData(2, ref maxTokens);
        return config is AnthropicConfig anthropic ? anthropic with { MaxTokens = maxTokens } : config;
    }
}
