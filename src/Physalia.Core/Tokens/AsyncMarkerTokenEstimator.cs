// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tokens;

/// <summary>
/// Base for token estimators whose counts can only be produced by an asynchronous API call.
/// These exist as distinct marker types so a component can select a counting strategy by type;
/// the synchronous <see cref="ITokenEstimator.Estimate"/> contract cannot be honoured, so it
/// throws and directs callers to the matching <see cref="AsyncTokenEstimation"/> method.
/// </summary>
public abstract class AsyncMarkerTokenEstimator : ITokenEstimator
{
    /// <summary>
    /// The <see cref="AsyncTokenEstimation"/> method that produces this estimator's counts,
    /// named in the thrown message (e.g. <c>CountAnthropicAsync</c>).
    /// </summary>
    protected abstract string AsyncMethodName { get; }

    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">
    /// Always thrown — use the async API named by <see cref="AsyncMethodName"/> instead.
    /// </exception>
    public int Estimate(Instructions instructions) =>
        throw new NotImplementedException(
            $"{GetType().Name} requires an async API call. Use AsyncTokenEstimation.{AsyncMethodName}.");
}
