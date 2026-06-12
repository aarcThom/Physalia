// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tokens;

/// <summary>
/// Marker type identifying the Gemini API token-counting strategy.
/// Exact counts are produced asynchronously via
/// <see cref="AsyncTokenEstimation.CountGeminiAsync"/>; this type is
/// passed as configuration to the dispatching component.
/// </summary>
public sealed class GeminiTokenEstimator : ITokenEstimator
{
    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">
    /// Always thrown — use <see cref="AsyncTokenEstimation.CountGeminiAsync"/> instead.
    /// </exception>
    public int Estimate(Instructions instructions) =>
        throw new NotImplementedException("GeminiTokenEstimator requires an async API call. Use AsyncTokenEstimation.CountGeminiAsync.");
}
