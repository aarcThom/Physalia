// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.Planning;
using Xunit;

namespace Physalia.Core.Tests.Planning;

public class BuildPlanStalePlanTests
{
    private const string Block = "<plan>\ngoal: A tower\n1. Base\n2. Shaft\nnow: 1\n</plan>";

    [Fact]
    public void StripPlanBlock_RemovesTheBlockAndKeepsTheDocument()
    {
        string response = Block + "\n{\"schema\":\"1.0\"}";

        string result = BuildPlanParser.StripPlanBlock(response);

        Assert.Equal("{\"schema\":\"1.0\"}", result);
    }

    [Fact]
    public void StripPlanBlock_KeepsProseOnBothSides()
    {
        string response = "before\n" + Block + "\nafter";

        Assert.Equal("before\n\nafter", BuildPlanParser.StripPlanBlock(response));
    }

    [Fact]
    public void StripPlanBlock_LeavesAResponseWithNoBlockAlone()
    {
        const string response = "just prose and {\"a\":1}";

        Assert.Equal(response, BuildPlanParser.StripPlanBlock(response));
    }

    [Fact]
    public void StripPlanBlock_RefusesAnUnterminatedBlock()
    {
        // Guessing where an unclosed block ends risks swallowing the document with it.
        const string response = "<plan>\ngoal: A tower\n1. Base\n{\"schema\":\"1.0\"}";

        Assert.Equal(response, BuildPlanParser.StripPlanBlock(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StripPlanBlock_HandlesEmptyInput(string? input)
    {
        Assert.Equal(input ?? string.Empty, BuildPlanParser.StripPlanBlock(input));
    }

    [Fact]
    public void ParseCurrentStage_ReadsABarePointerWithNoBlock()
    {
        // The steady state once the digest stops asking for a restatement.
        Assert.Equal(4, BuildPlanParser.ParseCurrentStage("now: 4\n{\"schema\":\"1.0\"}"));
    }

    [Theory]
    [InlineData("now: 3")]
    [InlineData("now:3")]
    [InlineData("**now: 3**")]
    [InlineData("current: 3")]
    [InlineData("current stage: 3")]
    [InlineData("now: stage 3")]
    public void ParseCurrentStage_AcceptsTheFormsTheBlockParserAccepts(string line)
    {
        Assert.Equal(3, BuildPlanParser.ParseCurrentStage(line + "\n{\"a\":1}"));
    }

    [Fact]
    public void ParseCurrentStage_IgnoresAPointerInsideTheDocument()
    {
        // A panel's text is canvas content the model authored, not a pointer aimed at Physalia.
        const string response = "{\n  \"panel\": \"now: 9\"\n}";

        Assert.Equal(0, BuildPlanParser.ParseCurrentStage(response));
    }

    [Fact]
    public void ParseCurrentStage_ReturnsZeroWhenAbsent()
    {
        Assert.Equal(0, BuildPlanParser.ParseCurrentStage("some prose\n{\"a\":1}"));
    }

    [Fact]
    public void RenderProgress_AsksForAPointerNotARestatement()
    {
        var plan = new BuildPlan("A tower", new[] { new BuildStage(1, "Base"), new BuildStage(2, "Shaft") }, 1);

        string digest = BuildPlanParser.RenderProgress(plan, 1);

        Assert.Contains("now: 2", digest, StringComparison.Ordinal);
        Assert.Contains("Do NOT restate the plan", digest, StringComparison.Ordinal);
        Assert.DoesNotContain("Restate your plan block", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderProgress_FinalStageStillAllowsProseButNotARestatement()
    {
        var plan = new BuildPlan("A tower", new[] { new BuildStage(1, "Base") }, 1);

        string digest = BuildPlanParser.RenderProgress(plan, 1);

        Assert.Contains("FINAL stage", digest, StringComparison.Ordinal);
        Assert.Contains("reply in plain prose", digest, StringComparison.Ordinal);
        Assert.DoesNotContain("restating", digest, StringComparison.Ordinal);
    }
}
