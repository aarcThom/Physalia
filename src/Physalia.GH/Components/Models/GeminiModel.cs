// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
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
        : base("Gemini Model", "Gemini", "Configures a Google Gemini model and fetches available models from the API.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6E412A9E-99CF-4CAC-9323-35B417FDA875");

    /// <inheritdoc/>
    protected override string ApiKeyDescription => "Google API key.";

    /// <inheritdoc/>
    protected override string ModelOutputDescription => "Configured Gemini model.";

    /// <inheritdoc/>
    protected override ModelConfig CreateConfig(string modelId, string apiKey)
        => new GeminiConfig(ModelId: modelId, ApiKey: apiKey);
}
