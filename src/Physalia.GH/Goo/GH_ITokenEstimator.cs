// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Tokens;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for an <see cref="ITokenEstimator"/>.
/// </summary>
public class GH_ITokenEstimator : PhyGoo<GH_ITokenEstimator, ITokenEstimator>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ITokenEstimator"/> class with no value.
    /// </summary>
    public GH_ITokenEstimator()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ITokenEstimator"/> class wrapping the given estimator.
    /// </summary>
    /// <param name="estimator">The token estimator to wrap.</param>
    public GH_ITokenEstimator(ITokenEstimator estimator)
        : base(estimator)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "Token Estimator";

    /// <inheritdoc/>
    public override string TypeDescription => "Estimates token consumption for a set of instructions.";

    /// <inheritdoc/>
    public override string ToString() => Value?.GetType().Name ?? string.Empty;
}
