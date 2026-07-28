// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Physalia.Core.Planning;
using Xunit;

namespace Physalia.Core.Tests.Planning;

public class BuildPlanParserTests
{
    private const string Response = """
        <plan>
        goal: A gabled house on the XY plane, 8m x 12m.
        1. Ground floor mass
        2. Gabled roof
        3. Window openings
        now: 2
        </plan>
        {"schema":"1.0","kind":"ghpatch","patch":{}}
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"schema\":\"1.0\",\"components\":[]}")]
    public void Parse_NoPlanBlock_ReturnsNull(string? response)
    {
        Assert.Null(BuildPlanParser.Parse(response));
    }

    [Fact]
    public void Parse_ReadsGoalStagesAndCurrent()
    {
        BuildPlan? plan = BuildPlanParser.Parse(Response);

        Assert.NotNull(plan);
        Assert.Equal("A gabled house on the XY plane, 8m x 12m.", plan!.Goal);
        Assert.Equal(3, plan.Stages.Count);
        Assert.Equal("Gabled roof", plan.Stages[1].Description);
        Assert.Equal(2, plan.CurrentStage);
    }

    [Fact]
    public void Parse_BlockWithNoStages_ReturnsNull()
    {
        Assert.Null(BuildPlanParser.Parse("<plan>\ngoal: something\n</plan>\n{}"));
    }

    // A model that opens the block and goes straight into the document must still be read.
    [Fact]
    public void Parse_UnterminatedBlock_StopsAtTheDocument()
    {
        BuildPlan? plan = BuildPlanParser.Parse(
            "<plan>\ngoal: a tower\n1. Base slab\n2. Shaft\nnow: 1\n{\"schema\":\"1.0\"}");

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Stages.Count);
        Assert.Equal(1, plan.CurrentStage);
    }

    [Theory]
    [InlineData("**goal:** a tower")]
    [InlineData("goal : a tower")]
    [InlineData("GOAL: a tower")]
    public void Parse_ToleratesDecoratedGoalLines(string goalLine)
    {
        BuildPlan? plan = BuildPlanParser.Parse($"<plan>\n{goalLine}\n1. Base\nnow: 1\n</plan>");

        Assert.NotNull(plan);
        Assert.Equal("a tower", plan!.Goal);
    }

    [Theory]
    [InlineData("1) Base slab")]
    [InlineData("stage 1 - Base slab")]
    [InlineData("1. Base slab")]
    [InlineData("1 Base slab")]
    public void Parse_ToleratesStageLineForms(string stageLine)
    {
        BuildPlan? plan = BuildPlanParser.Parse($"<plan>\n{stageLine}\n</plan>");

        Assert.NotNull(plan);
        Assert.Equal(new BuildStage(1, "Base slab"), plan!.Stages[0]);
    }

    // Out-of-order authoring must not scramble which stages read as built.
    [Fact]
    public void Parse_SortsStagesByNumber()
    {
        BuildPlan? plan = BuildPlanParser.Parse("<plan>\n3. Roof\n1. Slab\n2. Walls\n</plan>");

        Assert.NotNull(plan);
        Assert.Equal(new[] { 1, 2, 3 }, plan!.Stages.Select(s => s.Number));
    }

    [Fact]
    public void Parse_OmittedNow_ReportsZeroSoCallersHoldTheirStage()
    {
        BuildPlan? plan = BuildPlanParser.Parse("<plan>\n1. Slab\n2. Walls\n</plan>");

        Assert.NotNull(plan);
        Assert.Equal(0, plan!.CurrentStage);
    }

    [Fact]
    public void RenderProgress_MarksBuiltCurrentAndOutstanding()
    {
        BuildPlan plan = BuildPlanParser.Parse(Response)!;
        string digest = BuildPlanParser.RenderProgress(plan, plan.CurrentStage);

        Assert.Contains(BuildPlanParser.DigestMarker, digest);
        Assert.Contains("[built] 1. Ground floor mass", digest);
        Assert.Contains("[ NOW ] 2. Gabled roof", digest);
        Assert.Contains("[to do] 3. Window openings", digest);
        Assert.Contains("1 stage still to build", digest);
    }

    // The whole point of the digest: while stages remain, prose must be refused.
    [Fact]
    public void RenderProgress_WithStagesRemaining_ForbidsProseAndNamesTheNextStage()
    {
        BuildPlan plan = BuildPlanParser.Parse(Response)!;
        string digest = BuildPlanParser.RenderProgress(plan, plan.CurrentStage);

        Assert.Contains("Do NOT reply in prose", digest);
        Assert.Contains("stage 3 (Window openings)", digest);
    }

    [Fact]
    public void RenderProgress_OnFinalStage_InvitesProse()
    {
        BuildPlan plan = BuildPlanParser.Parse(Response)!;
        string digest = BuildPlanParser.RenderProgress(plan, 3);

        Assert.Contains("FINAL stage", digest);
        Assert.Contains("reply in plain prose", digest);
        Assert.DoesNotContain("still to build", digest);
    }
}
