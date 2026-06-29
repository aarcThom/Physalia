// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Tokens;

/// <summary>
/// Marker type identifying the Gemini API token-counting strategy. Exact counts are produced
/// asynchronously via <see cref="AsyncTokenEstimation.CountGeminiAsync"/>; the type carries no
/// synchronous <c>Estimate</c> (see <see cref="IAsyncTokenEstimator"/>).
/// </summary>
public sealed class GeminiTokenEstimator : IAsyncTokenEstimator
{
}
