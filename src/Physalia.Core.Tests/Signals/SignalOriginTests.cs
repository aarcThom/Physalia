// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Recording;
using Physalia.Core.Signals;
using Xunit;

namespace Physalia.Core.Tests.Signals;

/// <summary>
/// Provenance: which component a turn came FROM must survive the hops between producing the
/// feedback and recording it, or the chat window can only name the aggregator that re-minted it.
/// </summary>
public class SignalOriginTests
{
    private const string Sep = "\n";

    private static PhySignal From(Guid id, string name, string payload) =>
        PhySignal.Mint(SignalOutcome.Failure, payload, id, name);

    [Fact]
    public void OriginTrail_OfAnOriginalMint_IsTheEmittingComponent()
    {
        var id = Guid.NewGuid();
        PhySignal signal = From(id, "Geometry Report", "report");

        ComponentOrigin origin = Assert.Single(signal.OriginTrail);
        Assert.Equal(id, origin.Id);
        Assert.Equal("Geometry Report", origin.Name);
    }

    [Fact]
    public void Aggregation_CarriesEveryProducer_InOrder()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        AggregatedContent combined = SignalAggregation.Combine(
            new[] { From(first, "Geometry Report", "a"), From(second, "Runtime Health Check", "b") },
            Sep);

        Assert.Equal(new[] { first, second }, combined.Origins.Select(o => o.Id));
        Assert.Equal(new[] { "Geometry Report", "Runtime Health Check" }, combined.Origins.Select(o => o.Name));
    }

    [Fact]
    public void Aggregation_DeduplicatesTheSameProducer()
    {
        var id = Guid.NewGuid();

        AggregatedContent combined = SignalAggregation.Combine(
            new[] { From(id, "Geometry Report", "a"), From(id, "Geometry Report", "b") },
            Sep);

        Assert.Single(combined.Origins);
    }

    [Fact]
    public void ReMintedAggregate_KeepsTheProducers_NotTheAggregator()
    {
        // What Merge Signal / the Feedback Collector do: combine, then mint under their own identity.
        var producer = Guid.NewGuid();
        var aggregator = Guid.NewGuid();

        AggregatedContent combined = SignalAggregation.Combine(new[] { From(producer, "Geometry Report", "a") }, Sep);
        PhySignal merged = PhySignal.Mint(
            SignalOutcome.Failure, combined.Payload, aggregator, "Merge Signal", origins: combined.Origins);

        Assert.Equal(aggregator, merged.SourceId);
        Assert.Equal(producer, Assert.Single(merged.OriginTrail).Id);
    }

    [Fact]
    public void RecordedFeedbackTurn_CarriesItsProducer()
    {
        var id = Guid.NewGuid();
        Conversation start = Conversation.Empty.Append(new ConversationMessage(Role.User, "q"));

        RecordResult result = ConversationLogBuilder.Record(
            start,
            new[]
            {
                new RecordEvent(RecordedTurnKind.Response, From(Guid.NewGuid(), "LLM Call", "answer")),
                new RecordEvent(RecordedTurnKind.Feedback, From(id, "Geometry Report", "report")),
            });

        ConversationMessage feedback = result.Conversation.Messages[^1];
        Assert.True(feedback.IsFeedback);
        Assert.Equal(id, Assert.Single(feedback.Sources).Id);
    }

    [Fact]
    public void MergedUserTurn_UnionsSources_AndIsFeedbackOnlyWhenBothAre()
    {
        // A human prompt merged onto a feedback turn is not machine-generated: the merged turn must
        // not be presented as feedback, but both producers stay on the record.
        var reportId = Guid.NewGuid();
        Conversation start = Conversation.Empty.Append(
            new ConversationMessage(Role.User, "report")
            {
                IsFeedback = true,
                Sources = new[] { new ComponentOrigin(reportId, "Geometry Report") },
            });

        Conversation merged = start.MergeIntoLastUserMessage("typed by the human");

        Assert.False(merged.Messages[^1].IsFeedback);
        Assert.Equal(reportId, Assert.Single(merged.Messages[^1].Sources).Id);
    }

    [Fact]
    public void TwoFeedbackTurnsMerging_StayFeedback_AndNameBothProducers()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Conversation start = Conversation.Empty.Append(
            new ConversationMessage(Role.User, "report")
            {
                IsFeedback = true,
                Sources = new[] { new ComponentOrigin(first, "Geometry Report") },
            });

        Conversation merged = start.MergeIntoLastUserMessage(
            "health scan",
            incomingIsFeedback: true,
            incomingSources: new[] { new ComponentOrigin(second, "Runtime Health Check") });

        Assert.True(merged.Messages[^1].IsFeedback);
        Assert.Equal(new[] { first, second }, merged.Messages[^1].Sources.Select(o => o.Id));
    }
}
