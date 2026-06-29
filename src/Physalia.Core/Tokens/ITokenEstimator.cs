// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Tokens;

/// <summary>
/// Marker root for every token-counting strategy. It carries no method on purpose: some
/// strategies count synchronously (<see cref="ISyncTokenEstimator"/>) while others count only
/// via an asynchronous, configuration-bound API call (<see cref="IAsyncTokenEstimator"/>). Having
/// the two as distinct interfaces means a caller that needs a synchronous count takes an
/// <see cref="ISyncTokenEstimator"/> and the compiler rejects an API-backed estimator — replacing
/// the old runtime "this estimator must be counted asynchronously" throw with a compile-time error.
///
/// <para>This common root exists so a single Grasshopper parameter/goo can transport any estimator
/// on one wire; consumers narrow to the capability they need.</para>
/// </summary>
public interface ITokenEstimator
{
}
