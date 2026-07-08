// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Validation;
using Xunit;

namespace Physalia.Core.Tests.Validation;

public class SchemaValidatorTests
{
    private const string Schema =
        "{\"type\":\"object\",\"required\":[\"a\"],\"properties\":{\"a\":{\"type\":\"integer\"}}}";

    [Fact]
    public void Validate_ConformingJson_ReturnsOkWithOriginalJson()
    {
        const string json = "{\"a\":1}";

        Assert.True(SchemaValidator.Validate(json, Schema).IsOk(out string? value, out _));
        Assert.Equal(json, value);
    }

    [Fact]
    public void Validate_MissingRequiredProperty_ReturnsErrWithViolations()
    {
        Assert.True(SchemaValidator.Validate("{\"b\":2}", Schema).IsErr(out ValidationError? error, out _));
        Assert.NotEmpty(error!.Violations);
    }

    [Fact]
    public void Validate_WrongPropertyType_ReturnsErr()
    {
        Assert.True(SchemaValidator.Validate("{\"a\":\"text\"}", Schema).IsErr(out _, out _));
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsInvalidJsonError()
    {
        Assert.True(SchemaValidator.Validate("{not json", Schema).IsErr(out ValidationError? error, out _));

        // Model-facing wording: says what to do (emit one conforming JSON document) and keeps the
        // raw parse error as supporting detail.
        Assert.Contains("No parseable JSON document was found", error!.Message);
        Assert.Contains("Parse error:", error.Message);
    }

    [Fact]
    public void Validate_MalformedSchema_ReturnsInvalidSchemaError()
    {
        Assert.True(SchemaValidator.Validate("{\"a\":1}", "{not a schema").IsErr(out ValidationError? error, out _));
        Assert.Contains("Invalid schema", error!.Message);
    }

    [Fact]
    public void Validate_DisallowedProperty_NamesThePropertyAndLocation()
    {
        const string strict =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"a\":{\"type\":\"integer\"}}}";

        Assert.True(SchemaValidator.Validate("{\"a\":1,\"extra\":2}", strict).IsErr(out ValidationError? error, out _));

        // The library's "All values fail against the false schema" is rewritten to name the
        // offending property, so the model knows what to remove instead of guessing.
        Assert.Contains("property 'extra' is not allowed", error!.Message);
        Assert.DoesNotContain("false schema", error.Message);
    }

    [Fact]
    public void Validate_OneOfWithPropertyViolations_SuppressesRootUmbrellaNoise()
    {
        // Mirrors the real Node Graph schema shape: a root oneOf where every branch uses
        // additionalProperties:false. A stray property fails BOTH branches, historically producing
        // a wall of root-level oneOf/required noise around the one actionable line.
        const string oneOf =
            "{\"oneOf\":["
            + "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"a\"],\"properties\":{\"a\":{\"type\":\"integer\"},\"nested\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"x\":{\"type\":\"integer\"}}}}},"
            + "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"b\"],\"properties\":{\"b\":{\"type\":\"integer\"}}}"
            + "]}";

        Assert.True(SchemaValidator.Validate("{\"a\":1,\"nested\":{\"x\":1,\"paramName\":\"X\"}}", oneOf)
            .IsErr(out ValidationError? error, out _));

        Assert.Contains("property 'paramName' is not allowed at '/nested'", error!.Message);
        Assert.DoesNotContain("Expected 1 matching subschema", error.Message);
    }

    [Fact]
    public void Validate_GhpatchShapedDoc_DropsWrongBranchDiscriminatorNoise()
    {
        // Mirrors the real schema: oneOf where the "full document" branch knows nothing of
        // kind/patch. A ghpatch with one misplaced property must be told about THAT property only
        // — not that its kind/patch discriminator "is not allowed" (the other branch talking).
        const string oneOf =
            "{\"oneOf\":["
            + "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"kind\",\"patch\"],\"properties\":{"
            + "\"kind\":{\"const\":\"ghpatch\"},"
            + "\"patch\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"components\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"add\":{\"type\":\"array\"}}}}}}},"
            + "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"components\"],\"properties\":{\"components\":{\"type\":\"array\"}}}"
            + "]}";
        const string doc = "{\"kind\":\"ghpatch\",\"patch\":{\"components\":{\"add\":[],\"connections\":{}}}}";

        Assert.True(SchemaValidator.Validate(doc, oneOf).IsErr(out ValidationError? error, out _));

        Assert.Contains("property 'connections' is not allowed at '/patch/components'", error!.Message);
        Assert.DoesNotContain("property 'kind' is not allowed", error.Message);
        Assert.DoesNotContain("property 'patch' is not allowed", error.Message);
    }
}
