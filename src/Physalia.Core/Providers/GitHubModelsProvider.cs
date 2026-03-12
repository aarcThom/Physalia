// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Providers;

/// <summary>
/// GitHub Models implementation of <see cref="OpenAiCompatibleProvider"/>.
/// Provides free-tier access to models from OpenAI, Meta, Mistral and others
/// via GitHub's inference endpoint, authenticated with a GitHub Personal Access Token.
/// See: https://docs.github.com/github-models/prototyping-with-ai-models.
/// </summary>
internal class GitHubModelsProvider : OpenAiCompatibleProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubModelsProvider"/> class.
    /// </summary>
    /// <param name="apiKey">A GitHub Personal Access Token (PAT) used to authenticate requests.</param>
    public GitHubModelsProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the GitHub Models provider.
    /// </summary>
    public override string ProviderName => "github models";

    /// <summary>
    /// Gets the maximum number of output tokens for GitHub Models API requests.
    /// </summary>
    public override int MaxTokens => 4096;

    /// <summary>
    /// Gets the GitHub Models inference API base URL.
    /// </summary>
    protected override string BaseUrl => "https://models.github.ai/inference";
}
