// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// Utility methods for common string checks used across the pipeline.
/// </summary>
public static class StringHelpers
{
    /// <summary>
    /// Returns true if the string contains at least one non-whitespace character.
    /// </summary>
    /// <param name="s">The string to check.</param>
    /// <returns>true if the string is non-null and contains non-whitespace content; otherwise false.</returns>
    public static bool IsNonBlank(string? s) => !string.IsNullOrWhiteSpace(s);

    /// <summary>
    /// Returns true if two strings are equivalent after stripping all spaces and ignoring case.
    /// </summary>
    /// <param name="a">The first string.</param>
    /// <param name="b">The second string.</param>
    /// <returns>true if the strings are equal after normalisation; otherwise false.</returns>
    public static bool AreEquivalent(string? a, string? b) =>
        string.Equals(
            a?.Replace(" ", string.Empty),
            b?.Replace(" ", string.Empty),
            StringComparison.OrdinalIgnoreCase);
}
