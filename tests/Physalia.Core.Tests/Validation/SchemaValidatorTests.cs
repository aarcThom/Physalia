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
        Assert.Contains("Invalid JSON", error!.Message);
    }

    [Fact]
    public void Validate_MalformedSchema_ReturnsInvalidSchemaError()
    {
        Assert.True(SchemaValidator.Validate("{\"a\":1}", "{not a schema").IsErr(out ValidationError? error, out _));
        Assert.Contains("Invalid schema", error!.Message);
    }
}
