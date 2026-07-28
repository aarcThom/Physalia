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
                new ValidationError(
                    "No parseable JSON document was found in your response. Emit exactly ONE JSON "
                    + $"document conforming to the schema. Parse error: {ex.Message}",
                    Array.Empty<SchemaViolation>()));
        }

        EvaluationResults results;
        bool rootHasKind;
        using (instance)
        {
            rootHasKind = instance.RootElement.ValueKind == JsonValueKind.Object
                && instance.RootElement.TryGetProperty("kind", out _);
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

        violations = Humanize(violations, rootHasKind);

        string message = violations.Count > 0
            ? string.Join("; ", violations.Select(v => $"{v.Path}: {v.Message}"))
            : "JSON does not conform to schema.";

        return new Result<string, ValidationError>.Err(new ValidationError(message, violations));
    }

    /// <summary>
    /// Rewrites the JSON-schema library's opaque failure text into actionable feedback. An
    /// <c>additionalProperties: false</c> hit surfaces as "All values fail against the false
    /// schema" at the offending property's path — renamed here to say which property is not
    /// allowed and where. When such property-level violations exist, the root-level oneOf /
    /// required umbrella errors (one per non-matching branch) are dropped: they say nothing the
    /// property lines don't, and they dominated feedback with two dozen identical lines.
    ///
    /// <para>Wrong-oneOf-branch noise is suppressed in BOTH directions. A ghpatch tested against
    /// the full-document branch rejects <c>/kind</c> and <c>/patch</c>; a full document tested
    /// against the ghpatch branch rejects <c>/schema</c>, <c>/components</c>, <c>/connections</c>,
    /// and <c>/groups</c> "at the document root". Neither is actionable — the model, told to
    /// remove <c>components</c> from a full document, concludes the validator wanted a ghpatch and
    /// wobbles between document kinds. The full-document direction is dropped only when another
    /// violation survives: root-shape complaints as the ONLY output mean the document genuinely
    /// matched neither branch, and hiding them would leave an empty report.</para>
    /// </summary>
    /// <param name="violations">The raw violations from the evaluator.</param>
    /// <param name="rootHasKind">
    /// True when the instance root carries a <c>kind</c> property — the ghpatch discriminator.
    /// </param>
    /// <returns>The rewritten, deduplicated violations.</returns>
    private static List<SchemaViolation> Humanize(List<SchemaViolation> violations, bool rootHasKind)
    {
        const string falseSchema = "All values fail against the false schema";

        static bool IsRoot(string path) => string.IsNullOrEmpty(path) || path == "#" || path == "/";

        // A root-level property rejected by the oneOf branch the document was never meant to
        // match: the patch discriminator when the document IS a patch, the full-document keys
        // when it is NOT.
        bool IsWrongBranchNoise(SchemaViolation v) =>
            v.Message.Contains(falseSchema)
            && (rootHasKind
                ? v.Path is "/kind" or "/patch"
                : v.Path is "/schema" or "/components" or "/connections" or "/groups");

        static SchemaViolation RewriteNotAllowed(SchemaViolation v)
        {
            int slash = v.Path.LastIndexOf('/');
            if (slash < 0 || slash >= v.Path.Length - 1)
            {
                return v;
            }

            string property = v.Path[(slash + 1)..];
            string parent = slash == 0 ? "the document root" : $"'{v.Path[..slash]}'";
            return new SchemaViolation(
                v.Path,
                $"property '{property}' is not allowed at {parent} — remove it, or move it to where the schema defines it")
            {
                Kind = SchemaViolationKind.DisallowedProperty,
            };
        }

        bool hasPropertyLevel = violations.Any(v =>
            v.Message.Contains(falseSchema) && !IsRoot(v.Path) && !IsWrongBranchNoise(v));

        var rewritten = new List<SchemaViolation>();
        var wrongBranch = new List<SchemaViolation>();
        foreach (SchemaViolation v in violations)
        {
            if (IsWrongBranchNoise(v))
            {
                // The patch direction drops unconditionally (the discriminator can never be
                // "removed"); the full-document direction is parked and restored below when
                // nothing else survived.
                if (!rootHasKind)
                {
                    wrongBranch.Add(RewriteNotAllowed(v));
                }

                continue;
            }

            if (v.Message.Contains(falseSchema))
            {
                SchemaViolation renamed = RewriteNotAllowed(v);
                if (!ReferenceEquals(renamed, v))
                {
                    rewritten.Add(renamed);
                    continue;
                }
            }

            if (hasPropertyLevel && IsRoot(v.Path))
            {
                continue;
            }

            rewritten.Add(v);
        }

        if (rewritten.Count == 0)
        {
            rewritten.AddRange(wrongBranch);
        }

        return rewritten
            .GroupBy(v => (v.Path, v.Message))
            .Select(g => g.First())
            .ToList();
    }
}
