// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Providers.Concrete;

/// <summary>
/// OpenCode Zen implementation of <see cref="OpenAiCompatibleProvider"/>.
/// Provides access to a curated set of coding-agent-optimised models via the
/// OpenCode Zen unified API proxy.
/// See: https://opencode.ai/docs/zen/.
/// </summary>
internal class OpencodeZenProvider : OpenAiCompatibleProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpencodeZenProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the OpenCode Zen API.</param>
    public OpencodeZenProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the OpenCode Zen provider.
    /// </summary>
    public override string ProviderName => "opencode zen";

    /// <summary>
    /// Gets the OpenCode Zen API base URL.
    /// </summary>
    protected override string BaseUrl => "https://opencode.ai/zen/v1";
}
