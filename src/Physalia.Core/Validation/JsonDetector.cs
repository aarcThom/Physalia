// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text.RegularExpressions;

namespace Physalia.Core.Validation;

/// <summary>
/// Permissive heuristic for deciding whether raw LLM output contains attempted JSON at all —
/// even malformed or truncated JSON. This is a presence check, not a validator: parsing and
/// schema validation stay in <see cref="SchemaValidator"/> (the Auditor). The bias is
/// deliberate — when in doubt, report JSON present — because a false positive merely forwards
/// text the Auditor will reject with feedback, while a false negative would silently drop a
/// real-but-broken response out of the correction loop.
/// </summary>
public static class JsonDetector
{
    /// <summary>
    /// Matches a quoted-key-colon signature such as "components": — the structural fingerprint
    /// of an attempted JSON object, present even in truncated output.
    /// </summary>
    private static readonly Regex KeySignature = new("\"[^\"\r\n]*\"\\s*:", RegexOptions.Compiled);

    /// <summary>
    /// Reports whether the text appears to contain attempted JSON, however malformed.
    /// True when a ```json fence is present, the trimmed text starts with an opening brace
    /// or bracket, or a quoted-key-colon signature follows the first brace or bracket.
    /// </summary>
    /// <param name="text">Raw LLM output string.</param>
    /// <returns>True if the text looks like it contains attempted JSON; false for plain prose or blank input.</returns>
    public static bool ContainsJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // A ```json fence is an unambiguous declaration of intent, whatever it contains.
        if (text.IndexOf("```json", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        string trimmed = text.TrimStart();
        if (trimmed[0] == '{' || trimmed[0] == '[') return true;

        // A key signature after the first opening brace/bracket catches JSON embedded in
        // prose, including truncated output like: Here it is: {"a": [1, 2
        int objStart = text.IndexOf('{');
        int arrStart = text.IndexOf('[');
        int start = objStart >= 0 && (arrStart < 0 || objStart < arrStart) ? objStart : arrStart;

        return start >= 0 && KeySignature.IsMatch(text, start);
    }
}
