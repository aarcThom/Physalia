// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Validation;

/// <summary>
/// A single schema constraint violation.
/// </summary>
/// <param name="Path">JSON Pointer path to the violating node (e.g. <c>/components/0/id</c>).</param>
/// <param name="Message">Human-readable description of the violation.</param>
public record SchemaViolation(string Path, string Message);

/// <summary>
/// Describes a failed schema validation, including all individual violations.
/// </summary>
/// <param name="Message">Summary error message.</param>
/// <param name="Violations">Individual constraint violations.</param>
public record ValidationError(string Message, IReadOnlyList<SchemaViolation> Violations);
