// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Recording;
using Xunit;

namespace Physalia.Core.Tests.Recording;

public class RunLogTests
{
    private static RunRecord Record(
        int input = 100,
        int output = 50,
        bool ok = true,
        string? stop = null,
        string? error = null) =>
        new(
            new DateTime(2026, 9, 6, 14, 30, 0, DateTimeKind.Utc),
            "curious-cake-soap-fun",
            "claude-opus-5",
            input,
            output,
            1234,
            ok,
            stop,
            error);

    [Fact]
    public void ALineIsOneLine_BecauseThatIsTheWholePointOfTheFormat()
    {
        string line = RunLog.Line(Record());

        Assert.EndsWith("\n", line);
        Assert.Single(line.TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public void RecordsRoundTrip()
    {
        RunRecord source = Record(stop: "end_turn");

        RunRecord parsed = Assert.Single(RunLog.Parse(RunLog.Line(source)));

        Assert.Equal(source.When, parsed.When);
        Assert.Equal(source.Harness, parsed.Harness);
        Assert.Equal(source.Model, parsed.Model);
        Assert.Equal(source.InputTokens, parsed.InputTokens);
        Assert.Equal(source.OutputTokens, parsed.OutputTokens);
        Assert.Equal(source.DurationMs, parsed.DurationMs);
        Assert.True(parsed.Succeeded);
        Assert.Equal("end_turn", parsed.StopReason);
    }

    [Fact]
    public void OptionalFieldsAreOmittedWhenAbsent()
    {
        string line = RunLog.Line(Record());

        Assert.DoesNotContain("\"stop\"", line);
        Assert.DoesNotContain("\"error\"", line);
    }

    [Fact]
    public void AFailedCallKeepsItsError()
    {
        RunRecord parsed = Assert.Single(RunLog.Parse(RunLog.Line(Record(ok: false, error: "rate limited"))));

        Assert.False(parsed.Succeeded);
        Assert.Equal("rate limited", parsed.Error);
    }

    [Fact]
    public void AHalfWrittenLastLineCostsOneRecord_NotTheFile()
    {
        // Rhino killed mid-append. Months of records above it must survive.
        string text = RunLog.Line(Record()) + RunLog.Line(Record()) + "{\"when\":\"2026";

        Assert.Equal(2, RunLog.Parse(text).Count);
    }

    [Fact]
    public void BlankLinesAreIgnored()
    {
        Assert.Single(RunLog.Parse("\n\n" + RunLog.Line(Record()) + "\n\n"));
    }

    [Fact]
    public void EmptyInputParsesToNothing()
    {
        Assert.Empty(RunLog.Parse(null));
        Assert.Empty(RunLog.Parse("   "));
    }

    [Fact]
    public void SummaryAddsUpTheCallsTokensAndTime()
    {
        var records = new[] { Record(1_000, 500), Record(2_000, 500) };

        string text = RunLog.Summarise(records);

        Assert.Contains("2 calls", text);
        Assert.Contains("3k in", text);
        Assert.Contains("1k out", text);
    }

    [Fact]
    public void SummaryCallsOutFailures()
    {
        var records = new[] { Record(), Record(ok: false, error: "boom") };

        Assert.Contains("(1 failed)", RunLog.Summarise(records));
    }

    [Fact]
    public void SummaryOfNothingSaysSo()
    {
        Assert.Equal("No calls recorded.", RunLog.Summarise(Array.Empty<RunRecord>()));
        Assert.Equal("No calls recorded.", RunLog.Summarise(null));
    }
}
