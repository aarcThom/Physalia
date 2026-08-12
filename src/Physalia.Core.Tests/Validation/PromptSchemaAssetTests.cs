// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using GhJSON.Core;
using GhJSON.Core.DiffOperations;
using GhJSON.Core.PatchModels;
using GhJSON.Core.SchemaModels;
using Physalia.Core.Validation;
using Xunit;

namespace Physalia.Core.Tests.Validation;

/// <summary>
/// Guards the prompt schema assets in <c>Files/SYSTEM_PROMPTS/SCHEMA</c> against drift: every
/// example a schema file ships must validate against that schema, the umbrella must accept both
/// document kinds and route each to exactly one branch, and the GhJSON library's patch model
/// surface Physalia depends on must keep parsing the shapes the schemas teach the model.
/// </summary>
public class PromptSchemaAssetTests
{
    private static string SchemaDir
    {
        get
        {
            // Walk up from the test bin directory to the repo root's Files/SYSTEM_PROMPTS/SCHEMA.
            string? dir = AppContext.BaseDirectory;
            while (dir is not null)
            {
                string candidate = Path.Combine(dir, "Files", "SYSTEM_PROMPTS", "SCHEMA");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Files/SYSTEM_PROMPTS/SCHEMA was not found above the test directory.");
        }
    }

    private static string LoadSchema(string fileName) =>
        File.ReadAllText(Path.Combine(SchemaDir, fileName));

    // Every example embedded in a schema asset must validate against that asset.
    [Theory]
    [InlineData("Node Graph.json")]
    [InlineData("Incremental Node Graph.json")]
    [InlineData("Python3 Script.json")]
    [InlineData("C# Script.json")]
    public void SchemaAsset_OwnExamples_Validate(string fileName)
    {
        string schema = LoadSchema(fileName);
        using JsonDocument doc = JsonDocument.Parse(schema);

        Assert.True(doc.RootElement.TryGetProperty("examples", out JsonElement examples));
        foreach (JsonElement example in examples.EnumerateArray())
        {
            var result = SchemaValidator.Validate(example.GetRawText(), schema);
            Assert.True(
                result.IsOk(out _, out ValidationError? error),
                $"{fileName} example failed its own schema: {error?.Message} "
                + string.Join("; ", (error?.Violations ?? Array.Empty<SchemaViolation>()).Select(v => v.ToString())));
        }
    }

    [Fact]
    public void NodeGraphSchema_RejectsPatchMasqueradingAsDocument()
    {
        string schema = LoadSchema("Node Graph.json");

        // kind present but patch missing — matches NEITHER branch of the oneOf.
        Assert.True(SchemaValidator.Validate("{\"schema\":\"1.0\",\"kind\":\"ghpatch\"}", schema).IsErr(out _, out _));
    }

    [Fact]
    public void NodeGraphSchema_RejectsDocumentWithUnknownTopLevelField()
    {
        string schema = LoadSchema("Node Graph.json");

        Assert.True(SchemaValidator.Validate(
            "{\"schema\":\"1.0\",\"components\":[],\"prose\":\"hi\"}", schema).IsErr(out _, out _));
    }

    // The slider rule now spells out where 'rounding' goes, because a model put it one level up
    // and lost a round to "property 'rounding' is not allowed at .../componentState/extensions".
    // These two pin the shape the rule teaches: inside gh.numberslider, never beside it.
    [Theory]
    [InlineData("Node Graph.json")]
    [InlineData("Incremental Node Graph.json")]
    public void NodeGraphSchema_AcceptsRoundingInsideTheSliderExtension(string fileName)
    {
        const string document = "{\"schema\":\"1.0\",\"components\":[{\"name\":\"Number Slider\",\"id\":1,"
            + "\"pivot\":\"0,0\",\"componentState\":{\"extensions\":{\"gh.numberslider\":"
            + "{\"value\":\"5<0~10>\",\"rounding\":\"Integer\"}}}}]}";

        var result = SchemaValidator.Validate(document, LoadSchema(fileName));
        Assert.True(
            result.IsOk(out _, out ValidationError? error),
            $"the slider rule tells the model to write it this way: {error?.Message}");
    }

    [Theory]
    [InlineData("Node Graph.json")]
    [InlineData("Incremental Node Graph.json")]
    public void NodeGraphSchema_RejectsRoundingBesideTheSliderExtension(string fileName)
    {
        const string document = "{\"schema\":\"1.0\",\"components\":[{\"name\":\"Number Slider\",\"id\":1,"
            + "\"pivot\":\"0,0\",\"componentState\":{\"extensions\":{\"rounding\":\"Integer\","
            + "\"gh.numberslider\":{\"value\":\"5<0~10>\"}}}}]}";

        Assert.True(SchemaValidator.Validate(document, LoadSchema(fileName)).IsErr(out _, out _));
    }

    [Fact]
    public void NodeGraphSchema_RejectsMatchByNameOnly()
    {
        string schema = LoadSchema("Node Graph.json");
        const string patch = "{\"schema\":\"1.0\",\"kind\":\"ghpatch\",\"patch\":{\"components\":{\"remove\":[{\"name\":\"Circle\"}]}}}";

        // The house rule is instanceGuid-only matching; a name-based match must fail validation.
        Assert.True(SchemaValidator.Validate(patch, schema).IsErr(out _, out _));
    }

    // ---- Library surface pin: the GhJSON.Core patch API Physalia's placement layer depends on.

    private const string BaseDocument =
        "{\"schema\":\"1.0\",\"components\":[{\"name\":\"Number Slider\",\"id\":1,\"pivot\":\"0,0\"," +
        "\"instanceGuid\":\"11111111-1111-1111-1111-111111111111\"}]}";

    private const string ModifyPatch =
        "{\"schema\":\"1.0\",\"kind\":\"ghpatch\",\"patch\":{\"components\":{\"modify\":[" +
        "{\"match\":{\"instanceGuid\":\"11111111-1111-1111-1111-111111111111\"},\"set\":{\"nickName\":\"Radius\"}}]}}}";

    [Fact]
    public void GhJsonLibrary_PatchFromJson_RoundTrips()
    {
        GhPatchDocument patch = GhJson.PatchFromJson(ModifyPatch);

        Assert.Equal("ghpatch", patch.Kind);
        Assert.NotNull(patch.Patch.Components?.Modify);
        Assert.Single(patch.Patch.Components!.Modify!);
    }

    [Fact]
    public void GhJsonLibrary_ApplyPatch_ModifiesByInstanceGuid()
    {
        GhJsonDocument baseDoc = GhJson.FromJson(BaseDocument);
        GhPatchDocument patch = GhJson.PatchFromJson(ModifyPatch);

        ApplyPatchResult result = GhJson.ApplyPatch(baseDoc, patch, new ApplyPatchOptions
        {
            VerifyBase = false,
            ContinueOnConflict = true,
        });

        Assert.True(result.Success);
        Assert.Empty(result.Conflicts);
        Assert.Equal(1, result.ComponentsModified);
        Assert.Equal("Radius", result.Document.Components![0].NickName);
    }

    [Fact]
    public void GhJsonLibrary_ApplyPatch_ReportsMatchNotFound()
    {
        GhJsonDocument baseDoc = GhJson.FromJson(BaseDocument);
        GhPatchDocument patch = GhJson.PatchFromJson(
            "{\"kind\":\"ghpatch\",\"patch\":{\"components\":{\"remove\":[" +
            "{\"instanceGuid\":\"99999999-9999-9999-9999-999999999999\"}]}}}");

        ApplyPatchResult result = GhJson.ApplyPatch(baseDoc, patch, new ApplyPatchOptions
        {
            VerifyBase = false,
            ContinueOnConflict = true,
        });

        Assert.NotEmpty(result.Conflicts);
        Assert.Equal(0, result.ComponentsRemoved);
    }
}
