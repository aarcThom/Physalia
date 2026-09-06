// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Tools;
using Xunit;

namespace Physalia.Core.Tests.Tools;

/// <summary>
/// Covers the marker that separates a call the pipeline made from one the model made — which is what
/// decides whether a tool result may reach the conversation at all.
/// </summary>
public class ManualToolCallTests
{
    [Fact]
    public void A_minted_id_is_recognised_as_manual()
    {
        Assert.True(ManualToolCall.IsManual(ManualToolCall.NewId()));
    }

    [Fact]
    public void Minted_ids_are_distinct()
    {
        Assert.NotEqual(ManualToolCall.NewId(), ManualToolCall.NewId());
    }

    [Theory]
    [InlineData("toolu_01A9B")]
    [InlineData("call_abc123")]
    [InlineData("read_url")]
    [InlineData("")]
    [InlineData(null)]
    public void A_provider_issued_id_is_not_manual(string? id)
    {
        // No provider issues an id containing a colon, so the two vocabularies cannot overlap in
        // either direction.
        Assert.False(ManualToolCall.IsManual(id));
    }

    [Fact]
    public void An_empty_batch_is_not_a_manual_batch()
    {
        // "No calls" must not be mistaken for "all of them were mine" — the tool base already warns
        // about an empty dispatch, and treating it as manual would silence that.
        Assert.False(ManualToolCall.IsManualBatch(Array.Empty<ToolCallContent>()));
    }

    [Fact]
    public void A_batch_of_manual_calls_is_a_manual_batch()
    {
        Assert.True(ManualToolCall.IsManualBatch(new[] { Manual(), Manual() }));
    }

    [Fact]
    public void A_mixed_batch_is_treated_as_model_driven()
    {
        // Safe reading of a state that should not arise: the model's calls still get answered, where
        // the alternative is a round that never completes.
        Assert.False(ManualToolCall.IsManualBatch(new[] { Manual(), FromModel() }));
    }

    [Fact]
    public void A_batch_from_the_model_is_not_manual()
    {
        Assert.False(ManualToolCall.IsManualBatch(new[] { FromModel() }));
    }

    private static ToolCallContent Manual() =>
        new(ManualToolCall.NewId(), "api__vancouver", "{}");

    private static ToolCallContent FromModel() =>
        new("toolu_01XYZ", "api__vancouver", "{}");
}
