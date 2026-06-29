// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Tokens;
using Xunit;

namespace Physalia.Core.Tests.Tokens;

// Guards the item-6 split: synchronous estimators expose Estimate via ISyncTokenEstimator, while
// the API-backed ones are IAsyncTokenEstimator markers with no synchronous method. All share the
// ITokenEstimator root so one Grasshopper wire carries any of them.
public class TokenEstimatorTypeTests
{
    [Fact]
    public void HeuristicEstimator_IsSynchronous()
    {
        var estimator = new HeuristicTokenEstimator();

        Assert.IsAssignableFrom<ISyncTokenEstimator>(estimator);
        Assert.IsAssignableFrom<ITokenEstimator>(estimator);
        Assert.False(estimator is IAsyncTokenEstimator);
    }

    [Theory]
    [InlineData(typeof(AnthropicTokenEstimator))]
    [InlineData(typeof(GeminiTokenEstimator))]
    [InlineData(typeof(LlamaCppTokenEstimator))]
    public void ApiBackedEstimators_AreAsyncMarkers_NotSynchronous(Type estimatorType)
    {
        object estimator = Activator.CreateInstance(estimatorType)!;

        Assert.IsAssignableFrom<IAsyncTokenEstimator>(estimator);
        Assert.IsAssignableFrom<ITokenEstimator>(estimator);
        Assert.False(estimator is ISyncTokenEstimator,
            $"{estimatorType.Name} must not be a synchronous estimator — Estimate() would be a runtime trap.");
    }
}
