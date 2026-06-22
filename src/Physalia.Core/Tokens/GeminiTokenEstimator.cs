// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Tokens;

/// <summary>
/// Marker type identifying the Gemini API token-counting strategy. Exact counts are produced
/// asynchronously via <see cref="AsyncTokenEstimation.CountGeminiAsync"/>; see
/// <see cref="AsyncMarkerTokenEstimator"/> for why the synchronous path throws.
/// </summary>
public sealed class GeminiTokenEstimator : AsyncMarkerTokenEstimator
{
    /// <inheritdoc/>
    protected override string AsyncMethodName => nameof(AsyncTokenEstimation.CountGeminiAsync);
}
