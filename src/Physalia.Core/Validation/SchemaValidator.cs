// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using Json.Schema;
using Physalia.Core.Common;

namespace Physalia.Core.Validation;

/// <summary>
/// Pure functional JSON Schema validator.
/// </summary>
public static class SchemaValidator
{
    /// <summary>
    /// Validates <paramref name="json"/> against <paramref name="schema"/>.
    /// </summary>
    /// <param name="json">The JSON string to validate.</param>
    /// <param name="schema">A JSON Schema document (draft-07 or 2020-12).</param>
    /// <returns>
    /// <see cref="Result{T,E}.Ok"/> containing the original JSON on success, or
    /// <see cref="Result{T,E}.Err"/> containing a <see cref="ValidationError"/> on failure.
    /// </returns>
    public static Result<string, ValidationError> Validate(string json, string schema)
    {
        JsonSchema jsonSchema;
        try
        {
            jsonSchema = JsonSchema.FromText(schema);
        }
        catch (Exception ex)
        {
            return new Result<string, ValidationError>.Err(
                new ValidationError($"Invalid schema: {ex.Message}", Array.Empty<SchemaViolation>()));
        }

        JsonDocument instance;
        try
        {
            instance = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            return new Result<string, ValidationError>.Err(
                new ValidationError($"Invalid JSON: {ex.Message}", Array.Empty<SchemaViolation>()));
        }

        EvaluationResults results;
        using (instance)
        {
            var options = new EvaluationOptions { OutputFormat = OutputFormat.List };
            results = jsonSchema.Evaluate(instance.RootElement, options);
        }

        if (results.IsValid)
            return new Result<string, ValidationError>.Ok(json);

        var violations = (results.Details ?? Enumerable.Empty<EvaluationResults>())
            .Where(d => !d.IsValid && d.Errors != null)
            .SelectMany(d => d.Errors!.Select(
                kvp => new SchemaViolation(d.InstanceLocation.ToString(), $"{kvp.Key}: {kvp.Value}")))
            .ToList();

        string message = violations.Count > 0
            ? string.Join("; ", violations.Select(v => $"{v.Path}: {v.Message}"))
            : "JSON does not conform to schema.";

        return new Result<string, ValidationError>.Err(new ValidationError(message, violations));
    }
}
