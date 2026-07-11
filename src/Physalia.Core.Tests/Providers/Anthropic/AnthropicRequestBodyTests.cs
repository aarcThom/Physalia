// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models.Named;
using Physalia.Core.Providers.Anthropic;
using Xunit;

namespace Physalia.Core.Tests.Providers.Anthropic;

public class AnthropicRequestBodyTests
{
    private sealed class TestableAnthropicProvider : AnthropicProtocolProvider
    {
        public JsonObject Build(Conversation conversation, AnthropicConfig config, IReadOnlyList<ToolDefinition>? tools = null)
            => BuildRequestBody(conversation, "system", config, tools);
    }

    private static readonly Conversation SingleUserTurn =
        Conversation.Empty.Append(new ConversationMessage(Role.User, "hi"));

    private static JsonObject Build(AnthropicConfig config, Conversation? conversation = null)
        => new TestableAnthropicProvider().Build(conversation ?? SingleUserTurn, config);

    [Fact]
    public void Build_ThinkingBudgetSet_AddsThinkingAndOmitsSampling()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-4-5", "key", MaxTokens: 8192, ThinkingBudget: 2048));

        Assert.Equal("enabled", body["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal(2048, body["thinking"]!["budget_tokens"]!.GetValue<int>());
        Assert.Equal(8192, body["max_tokens"]!.GetValue<int>());
        Assert.False(body.ContainsKey("temperature"));
        Assert.False(body.ContainsKey("top_p"));
        Assert.False(body.ContainsKey("top_k"));
    }

    [Fact]
    public void Build_BudgetBelowMinimum_ClampedTo1024()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-4-5", "key", ThinkingBudget: 100));

        Assert.Equal(1024, body["thinking"]!["budget_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_BudgetAtOrAboveMaxTokens_BumpsMaxTokens()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-4-5", "key", MaxTokens: 8192, ThinkingBudget: 8192));

        Assert.Equal(8192, body["thinking"]!["budget_tokens"]!.GetValue<int>());
        Assert.Equal(12288, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_AdaptiveSentinel_AddsAdaptiveThinkingWithSummarizedDisplay()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-5", "key", MaxTokens: 8192, ThinkingBudget: -1));

        Assert.Equal("adaptive", body["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("summarized", body["thinking"]!["display"]!.GetValue<string>());
        Assert.False(body["thinking"]!.AsObject().ContainsKey("budget_tokens"));
        Assert.Equal(8192, body["max_tokens"]!.GetValue<int>());
        Assert.False(body.ContainsKey("temperature"));
        Assert.False(body.ContainsKey("top_p"));
        Assert.False(body.ContainsKey("top_k"));
    }

    [Fact]
    public void Build_NoBudget_OmitsThinkingKeepsTemperature()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-4-5", "key", Temperature: 0.7f, TopK: 40));

        Assert.False(body.ContainsKey("thinking"));
        Assert.Equal(0.7f, body["temperature"]!.GetValue<float>(), 3);
        Assert.Equal(40, body["top_k"]!.GetValue<int>());
    }

    [Fact]
    public void Build_Sonnet5_Unspecified_DefaultsToAdaptiveSummarizedNoSampling()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-5", "key", Temperature: 0.7f, TopK: 40));

        Assert.Equal("adaptive", body["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("summarized", body["thinking"]!["display"]!.GetValue<string>());
        Assert.False(body.ContainsKey("temperature"));
        Assert.False(body.ContainsKey("top_p"));
        Assert.False(body.ContainsKey("top_k"));
    }

    [Fact]
    public void Build_Sonnet5_ExplicitZero_SendsThinkingDisabled()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-5", "key", ThinkingBudget: 0));

        Assert.Equal("disabled", body["thinking"]!["type"]!.GetValue<string>());
        Assert.False(body["thinking"]!.AsObject().ContainsKey("display"));
    }

    [Fact]
    public void Build_Sonnet5_ManualBudget_MappedToAdaptive()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-5", "key", ThinkingBudget: 2048));

        Assert.Equal("adaptive", body["thinking"]!["type"]!.GetValue<string>());
        Assert.False(body["thinking"]!.AsObject().ContainsKey("budget_tokens"));
    }

    [Fact]
    public void Build_OlderModel_AdaptiveSentinel_MappedToManualDefaultBudget()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-4-5", "key", MaxTokens: 16000, ThinkingBudget: -1));

        Assert.Equal("enabled", body["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal(8192, body["thinking"]!["budget_tokens"]!.GetValue<int>());
        Assert.Equal(16000, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_Fable_ExplicitZero_SendsNoThinkingConfig()
    {
        // Fable cannot disable thinking; the closest approximation of "off" is no
        // config at all (display stays omitted).
        JsonObject body = Build(new AnthropicConfig("claude-fable-5", "key", ThinkingBudget: 0));

        Assert.False(body.ContainsKey("thinking"));
        Assert.False(body.ContainsKey("temperature"));
    }

    [Fact]
    public void Build_Sonnet46_Unspecified_SendsNoThinkingKeepsSampling()
    {
        JsonObject body = Build(new AnthropicConfig("claude-sonnet-4-6", "key", Temperature: 0.5f));

        Assert.False(body.ContainsKey("thinking"));
        Assert.Equal(0.5f, body["temperature"]!.GetValue<float>(), 3);
    }

    [Fact]
    public void Build_AssistantHistoryWithThinkTags_Stripped()
    {
        Conversation conversation = Conversation.Empty
            .Append(new ConversationMessage(Role.User, "hi"))
            .Append(new ConversationMessage(Role.Assistant, "<think>reasoning</think>\n\nanswer"))
            .Append(new ConversationMessage(Role.User, "again"));

        JsonObject body = Build(new AnthropicConfig("claude-sonnet-4-5", "key"), conversation);

        var messages = body["messages"]!.AsArray();
        string assistantContent = messages[1]!["content"]!.GetValue<string>();
        Assert.Equal("answer", assistantContent);
    }

    [Fact]
    public void Build_ThinkingOnlyAssistantTurn_SerializesPlaceholderNotEmpty()
    {
        Conversation conversation = Conversation.Empty
            .Append(new ConversationMessage(Role.User, "hi"))
            .Append(new ConversationMessage(Role.Assistant, "<think>only reasoning</think>"))
            .Append(new ConversationMessage(Role.User, "again"));

        JsonObject body = Build(new AnthropicConfig("claude-sonnet-4-5", "key"), conversation);

        var messages = body["messages"]!.AsArray();
        Assert.Equal(3, messages.Count);
        string assistantContent = messages[1]!["content"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(assistantContent));
        Assert.DoesNotContain("<think", assistantContent, StringComparison.OrdinalIgnoreCase);
    }
}
