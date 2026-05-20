// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tokens;

/// <summary>
/// Estimates the number of tokens consumed by an <see cref="Instructions"/> object.
/// Implementations may be exact (tiktoken) or approximate (heuristic).
/// </summary>
public interface ITokenEstimator
{
    /// <summary>
    /// Returns an estimated token count for the given instructions,
    /// including the system prompt and all conversation messages.
    /// </summary>
    /// <param name="instructions">The instructions to measure.</param>
    /// <returns>Estimated token count.</returns>
    int Estimate(Instructions instructions);
}
