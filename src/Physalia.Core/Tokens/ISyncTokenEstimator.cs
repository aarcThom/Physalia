// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tokens;

/// <summary>
/// A token estimator whose count can be produced synchronously, with no API call — e.g. the
/// character-count heuristic or a local tiktoken vocabulary. Code that must measure inline (the
/// deterministic compactor, the token-threshold gate) depends on this interface so the compiler
/// guarantees it never receives an API-backed estimator that can only count asynchronously.
/// </summary>
public interface ISyncTokenEstimator : ITokenEstimator
{
    /// <summary>
    /// Returns an estimated token count for the given instructions,
    /// including the system prompt and all conversation messages.
    /// </summary>
    /// <param name="instructions">The instructions to measure.</param>
    /// <returns>Estimated token count.</returns>
    int Estimate(Instructions instructions);
}
