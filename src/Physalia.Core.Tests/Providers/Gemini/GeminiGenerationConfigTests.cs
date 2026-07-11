// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using Physalia.Core.Models.Named;
using Physalia.Core.Providers.Gemini;
using Xunit;

namespace Physalia.Core.Tests.Providers.Gemini;

public class GeminiGenerationConfigTests
{
    private sealed class TestableGeminiProvider : GeminiProtocolProvider
    {
        public JsonObject Build(GeminiConfig config) => BuildGenerationConfig(config);
    }

    private static JsonObject Build(GeminiConfig config) => new TestableGeminiProvider().Build(config);

    [Fact]
    public void Build_ThinkingModel_Unspecified_IncludesThoughtsWithoutBudget()
    {
        JsonObject cfg = Build(new GeminiConfig("gemini-2.5-flash", "key"));

        Assert.True(cfg["thinkingConfig"]!["includeThoughts"]!.GetValue<bool>());
        Assert.False(cfg["thinkingConfig"]!.AsObject().ContainsKey("thinkingBudget"));
    }

    [Fact]
    public void Build_OlderModel_Unspecified_OmitsThinkingConfig()
    {
        JsonObject cfg = Build(new GeminiConfig("gemini-2.0-flash", "key"));

        Assert.False(cfg.ContainsKey("thinkingConfig"));
    }

    [Fact]
    public void Build_ExplicitBudget_SendsBudgetAndIncludeThoughts()
    {
        JsonObject cfg = Build(new GeminiConfig("gemini-2.0-flash", "key", ThinkingBudget: 1024));

        Assert.Equal(1024, cfg["thinkingConfig"]!["thinkingBudget"]!.GetValue<int>());
        Assert.True(cfg["thinkingConfig"]!["includeThoughts"]!.GetValue<bool>());
    }
}
