// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Tokens;

/// <summary>
/// Marker for a token-counting strategy whose exact count is produced by an asynchronous,
/// provider-specific API call. These types intentionally carry no synchronous <c>Estimate</c>
/// method: the actual count requires a provider configuration and an <c>HttpClient</c> and is
/// produced by the matching <see cref="AsyncTokenEstimation"/> method, selected by the consumer on
/// the estimator's concrete type. The marker lets such an estimator travel the same Grasshopper
/// wire as a synchronous one while making "cannot be counted synchronously" a compile-time fact.
/// </summary>
public interface IAsyncTokenEstimator : ITokenEstimator
{
}
