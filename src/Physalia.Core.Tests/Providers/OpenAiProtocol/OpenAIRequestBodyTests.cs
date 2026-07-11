// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models.Named;
using Physalia.Core.Providers.OpenAiProtocol;
using Xunit;

namespace Physalia.Core.Tests.Providers.OpenAiProtocol;

public class OpenAIRequestBodyTests
{
    private sealed class TestableOpenAIProvider : OpenAIProtocolProvider
    {
        public JsonObject Build(OpenAICompatibleConfig config, Conversation? conversation = null, IReadOnlyList<ToolDefinition>? tools = null)
            => BuildRequestBody(
                conversation ?? Conversation.Empty.Append(new ConversationMessage(Role.User, "hi")),
                "system",
                config,
                tools);
    }

    private static JsonObject Build(OpenAICompatibleConfig config)
        => new TestableOpenAIProvider().Build(config);

    [Fact]
    public void Build_ThinkingEnabled_AddsThinkingObject()
    {
        JsonObject body = Build(new OpenAICompatibleConfig("some-unknown-model", ThinkingEnabled: true));

        Assert.Equal("enabled", body["thinking"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Build_Default_OmitsThinkingAndReasoningEffortKeepsSampling()
    {
        JsonObject body = Build(new OpenAICompatibleConfig("gpt-4o"));

        Assert.False(body.ContainsKey("thinking"));
        Assert.False(body.ContainsKey("reasoning_effort"));
        Assert.True(body.ContainsKey("max_tokens"));
        Assert.False(body.ContainsKey("max_completion_tokens"));
        Assert.True(body.ContainsKey("temperature"));
        Assert.True(body.ContainsKey("top_p"));
    }

    [Fact]
    public void Build_DeepSeekV4_Unspecified_DefaultsToThinkingEnabled()
    {
        JsonObject body = Build(new OpenAICompatibleConfig("deepseek-v4-pro"));

        Assert.Equal("enabled", body["thinking"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Build_DeepSeekV4_ExplicitFalse_OmitsThinking()
    {
        JsonObject body = Build(new OpenAICompatibleConfig("deepseek-v4-flash", ThinkingEnabled: false));

        Assert.False(body.ContainsKey("thinking"));
    }

    [Fact]
    public void Build_OpenAIReasoningModel_UsesMaxCompletionTokensAndOmitsSampling()
    {
        JsonObject body = Build(new OpenAICompatibleConfig("gpt-5"));

        Assert.False(body.ContainsKey("max_tokens"));
        Assert.Equal(4096, body["max_completion_tokens"]!.GetValue<int>());
        Assert.False(body.ContainsKey("temperature"));
        Assert.False(body.ContainsKey("top_p"));
    }

    [Fact]
    public void Build_NamespacedReasoningModel_ResolvedAfterStrippingNamespace()
    {
        JsonObject body = Build(new OpenAICompatibleConfig("openai/o3-mini"));

        Assert.True(body.ContainsKey("max_completion_tokens"));
        Assert.False(body.ContainsKey("temperature"));
    }

    [Fact]
    public void Build_ReasoningEffortSet_AddsField()
    {
        JsonObject body = Build(new OpenAICompatibleConfig("gpt-5", ReasoningEffort: "high"));

        Assert.Equal("high", body["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public void Build_AssistantHistoryWithThinkTags_Stripped()
    {
        Conversation conversation = Conversation.Empty
            .Append(new ConversationMessage(Role.User, "hi"))
            .Append(new ConversationMessage(Role.Assistant, "<think>reasoning</think>\n\nanswer"))
            .Append(new ConversationMessage(Role.User, "again"));

        JsonObject body = new TestableOpenAIProvider().Build(new OpenAICompatibleConfig("gpt-4o"), conversation);

        var messages = body["messages"]!.AsArray();

        // messages[0] is the system prompt; [2] is the assistant turn.
        Assert.Equal("answer", messages[2]!["content"]!.GetValue<string>());
    }
}
