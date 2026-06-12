// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tokens;

/// <summary>
/// Marker type identifying the Anthropic API token-counting strategy.
/// Exact counts are produced asynchronously via
/// <see cref="AsyncTokenEstimation.CountAnthropicAsync"/>; this type is
/// passed as configuration to the dispatching component.
/// </summary>
public sealed class AnthropicTokenEstimator : ITokenEstimator
{
    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">
    /// Always thrown — use <see cref="AsyncTokenEstimation.CountAnthropicAsync"/> instead.
    /// </exception>
    public int Estimate(Instructions instructions) =>
        throw new NotImplementedException("AnthropicTokenEstimator requires an async API call. Use AsyncTokenEstimation.CountAnthropicAsync.");
}
