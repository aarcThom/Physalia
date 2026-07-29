// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text.Json.Nodes;
using Physalia.Core.Validation;
using Xunit;

namespace Physalia.Core.Tests.Validation;

public class SchemaRepairTests
{
    private static SchemaViolation Disallowed(string path) =>
        new(path, "property is not allowed") { Kind = SchemaViolationKind.DisallowedProperty };

    [Fact]
    public void DropsTheStrayProperty_AndLeavesEverythingElseIntact()
    {
        // The exact shape observed live: a ghpatch with an invented placeholder key under /patch.
        const string json = """
        {"schema":"1.0","kind":"ghpatch","patch":{"base":{"checksum":"sha256-abc"},"groups_note_placeholder":"note","components":{"add":[{"id":1}]}}}
        """;

        RepairOutcome? outcome = SchemaRepair.DropDisallowedProperties(
            json,
            new[] { Disallowed("/patch/groups_note_placeholder") });

        Assert.NotNull(outcome);
        Assert.Equal(new[] { "/patch/groups_note_placeholder" }, outcome!.RemovedPaths);

        JsonNode repaired = JsonNode.Parse(outcome.Json)!;
        Assert.Null(repaired["patch"]!["groups_note_placeholder"]);
        Assert.Equal("sha256-abc", repaired["patch"]!["base"]!["checksum"]!.GetValue<string>());
        Assert.Equal(1, repaired["patch"]!["components"]!["add"]![0]!["id"]!.GetValue<int>());
    }

    [Fact]
    public void RemovesEveryReportedProperty()
    {
        const string json = """{"a":{"x":1,"bad1":2},"bad2":3}""";

        RepairOutcome? outcome = SchemaRepair.DropDisallowedProperties(
            json,
            new[] { Disallowed("/a/bad1"), Disallowed("/bad2") });

        Assert.NotNull(outcome);
        Assert.Equal(2, outcome!.RemovedPaths.Count);

        JsonNode repaired = JsonNode.Parse(outcome.Json)!;
        Assert.Null(repaired["bad2"]);
        Assert.Null(repaired["a"]!["bad1"]);
        Assert.Equal(1, repaired["a"]!["x"]!.GetValue<int>());
    }

    [Fact]
    public void RefusesWhenAnyViolationIsNotADisallowedProperty()
    {
        // A real defect alongside a stray key: repairing half would hand the model feedback about
        // a document it never wrote.
        const string json = """{"bad":1,"count":"not-a-number"}""";

        RepairOutcome? outcome = SchemaRepair.DropDisallowedProperties(
            json,
            new[] { Disallowed("/bad"), new SchemaViolation("/count", "expected integer") });

        Assert.Null(outcome);
    }

    [Fact]
    public void RefusesWhenThereAreNoViolations()
    {
        Assert.Null(SchemaRepair.DropDisallowedProperties("""{"a":1}""", Array.Empty<SchemaViolation>()));
    }

    [Fact]
    public void RefusesUnparseableJson()
    {
        Assert.Null(SchemaRepair.DropDisallowedProperties("{ not json", new[] { Disallowed("/a") }));
    }

    [Fact]
    public void RefusesWhenNothingActuallyMatchedThePointer()
    {
        Assert.Null(SchemaRepair.DropDisallowedProperties("""{"a":1}""", new[] { Disallowed("/nope/deeper") }));
    }

    [Fact]
    public void WillNotDeleteAnArrayElement()
    {
        // Deleting by position silently renumbers everything after it — never safe to do behind
        // the model's back.
        const string json = """{"items":[{"id":1},{"id":2}]}""";

        Assert.Null(SchemaRepair.DropDisallowedProperties(json, new[] { Disallowed("/items/0") }));
    }

    [Fact]
    public void ResolvesPointerEscapes()
    {
        const string json = """{"odd/key":{"a~b":1,"keep":2}}""";

        RepairOutcome? outcome = SchemaRepair.DropDisallowedProperties(
            json,
            new[] { Disallowed("/odd~1key/a~0b") });

        Assert.NotNull(outcome);
        JsonNode repaired = JsonNode.Parse(outcome!.Json)!;
        Assert.Null(repaired["odd/key"]!["a~b"]);
        Assert.Equal(2, repaired["odd/key"]!["keep"]!.GetValue<int>());
    }

    [Fact]
    public void ReachesAPropertyNestedUnderAnArrayElement()
    {
        const string json = """{"add":[{"id":1,"bogus":true}]}""";

        RepairOutcome? outcome = SchemaRepair.DropDisallowedProperties(
            json,
            new[] { Disallowed("/add/0/bogus") });

        Assert.NotNull(outcome);
        JsonNode repaired = JsonNode.Parse(outcome!.Json)!;
        Assert.Null(repaired["add"]![0]!["bogus"]);
        Assert.Equal(1, repaired["add"]![0]!["id"]!.GetValue<int>());
    }
}
