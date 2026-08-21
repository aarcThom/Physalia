// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Validation;

/// <summary>
/// What kind of defect a violation describes. Only the distinctions the pipeline acts on are
/// modelled — everything else is <see cref="Other"/>.
/// </summary>
public enum SchemaViolationKind
{
    /// <summary>
    /// A violation that has to go back to the model.
    /// </summary>
    Other,

    /// <summary>
    /// A property the schema does not allow at that location — an <c>additionalProperties: false</c>
    /// hit. Purely additive: the document is otherwise conformant, and deleting the property makes
    /// it valid without changing anything the model meant to express.
    /// </summary>
    DisallowedProperty,
}

/// <summary>
/// A single schema constraint violation.
/// </summary>
/// <param name="Path">JSON Pointer to the offending location in the instance (e.g. <c>/components/0/id</c>).</param>
/// <param name="Message">Human-readable description of the defect.</param>
public record SchemaViolation(string Path, string Message)
{
    /// <summary>
    /// Gets what kind of defect this is, for callers that can resolve some kinds without a round
    /// trip. Defaults to <see cref="SchemaViolationKind.Other"/> — the safe reading, which sends
    /// the violation back to the model.
    /// </summary>
    public SchemaViolationKind Kind { get; init; } = SchemaViolationKind.Other;
}

/// <summary>
/// Describes a failed schema validation, including all individual violations.
/// </summary>
/// <param name="Message">Summary error message.</param>
/// <param name="Violations">Individual constraint violations.</param>
public record ValidationError(string Message, IReadOnlyList<SchemaViolation> Violations);
