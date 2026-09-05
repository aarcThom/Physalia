// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.Config;
using Physalia.Core.Models;
using Physalia.Core.Models.Named;

namespace Physalia.GH.Components;

/// <summary>
/// Grasshopper component that configures a Google Gemini model.
/// Fetches available models from the API and exposes them via <see cref="IPickableValuesSource"/>.
/// </summary>
public class GeminiModel : ModelComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiModel"/> class.
    /// </summary>
    public GeminiModel()
        : base("Gemini Model", "Gemini", "Points the pipeline at a Google Gemini model. The list of models on offer is fetched from the API as soon as a key arrives.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6E412A9E-99CF-4CAC-9323-35B417FDA875");

    /// <inheritdoc/>
    protected override string ModelApiDescription =>
        "Your Google AI endpoint and key. Wire a Model API component; the model list is fetched the moment it arrives.";

    /// <inheritdoc/>
    protected override string ModelIdDescription =>
        "Which Gemini to use, e.g. gemini-2.5-pro. The Picker placed alongside fills with whatever the key can reach.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription =>
        "The Gemini model, configured. Wire into an LLM Call, or through a Gemini Tweaker first to change how it samples.";

    /// <inheritdoc/>
    protected override ModelConfig CreateConfig(string modelId, ModelApi api)
        => new GeminiConfig(
            ModelId: modelId,
            ApiKey: api.Key,
            BaseUrl: api.BaseUrlOr("https://generativelanguage.googleapis.com/v1beta"));
}
