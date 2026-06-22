// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Tokens;

/// <summary>
/// Marker type identifying the Anthropic API token-counting strategy. Exact counts are produced
/// asynchronously via <see cref="AsyncTokenEstimation.CountAnthropicAsync"/>; see
/// <see cref="AsyncMarkerTokenEstimator"/> for why the synchronous path throws.
/// </summary>
public sealed class AnthropicTokenEstimator : AsyncMarkerTokenEstimator
{
    /// <inheritdoc/>
    protected override string AsyncMethodName => nameof(AsyncTokenEstimation.CountAnthropicAsync);
}
