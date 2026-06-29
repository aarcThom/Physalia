// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Python;
using Xunit;

namespace Physalia.Core.Tests.Python;

public class PythonOutputAccessInferenceTests
{
    private static bool Infers(string code, string name) =>
        PythonOutputAccessInference.InferListVariables(code, new[] { name }).Contains(name);

    [Theory]
    [InlineData("pts = [1, 2, 3]")]
    [InlineData("pts = [x for x in range(3)]")]
    [InlineData("pts = list(range(3))")]
    [InlineData("pts += [4]")]
    [InlineData("    pts = [1]")]            // indented assignment
    [InlineData("pts.append(1)")]
    [InlineData("pts.extend(other)")]
    [InlineData("pts.insert(0, 1)")]
    public void InferListVariables_DetectsListAssignments(string code)
    {
        Assert.True(Infers(code, "pts"));
    }

    [Theory]
    [InlineData("pts = 5")]
    [InlineData("pts = compute_single_value()")]
    [InlineData("pts = \"a string\"")]
    public void InferListVariables_IgnoresNonListAssignments(string code)
    {
        Assert.False(Infers(code, "pts"));
    }

    [Fact]
    public void InferListVariables_IgnoresFullyCommentedAssignment()
    {
        // The line-start anchor means a leading '#' is never treated as an assignment.
        Assert.False(Infers("# pts = [1, 2, 3]", "pts"));
    }

    [Fact]
    public void InferListVariables_DoesNotMatchLongerIdentifier()
    {
        // "pts" must not be found inside "points".
        Assert.False(Infers("points = [1, 2, 3]", "pts"));
        Assert.False(Infers("points.append(1)", "pts"));
    }

    [Fact]
    public void InferListVariables_ReturnsOnlyListNamesFromMixedSet()
    {
        const string code = "a = [1, 2]\nb = 5\nc = list(x)";

        IReadOnlyCollection<string> result =
            PythonOutputAccessInference.InferListVariables(code, new[] { "a", "b", "c" });

        Assert.Contains("a", result);
        Assert.DoesNotContain("b", result);
        Assert.Contains("c", result);
    }

    [Fact]
    public void InferListVariables_EmptyCode_ReturnsEmpty()
    {
        Assert.Empty(PythonOutputAccessInference.InferListVariables(string.Empty, new[] { "pts" }));
    }
}
