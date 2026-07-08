// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Physalia.Core.Validation;

/// <summary>
/// Decides whether a piece of LLM output is a ghpatch document rather than a full GhJSON
/// document, by sniffing the top-level <c>"kind": "ghpatch"</c> discriminator. This is a routing
/// check, not a validator: the placement layer parses and validates the patch properly and is the
/// final authority. Like <see cref="JsonDetector"/>, the bias is permissive — malformed JSON that
/// still declares the discriminator routes to the patch path, where real parsing produces a
/// correction-loop error instead of the text silently falling through to full-document placement
/// (which would misread every patch as a graph of components).
/// </summary>
public static class GhPatchDetector
{
    /// <summary>
    /// Matches the <c>"kind": "ghpatch"</c> discriminator, used when the text does not parse as
    /// JSON (truncated or otherwise malformed output that still declares its intent).
    /// </summary>
    private static readonly Regex KindSignature = new(
        "\"kind\"\\s*:\\s*\"ghpatch\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Reports whether the text is a ghpatch document. True when the text parses as a JSON object
    /// whose top-level <c>kind</c> property is <c>ghpatch</c>, or — for unparseable text — when a
    /// <c>"kind": "ghpatch"</c> discriminator appears anywhere in it.
    /// </summary>
    /// <param name="text">Raw or extracted LLM output string.</param>
    /// <returns>True if the text declares itself a ghpatch; false for full GhJSON documents, plain prose, or blank input.</returns>
    public static bool IsGhPatch(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            return doc.RootElement.TryGetProperty("kind", out JsonElement kind)
                && kind.ValueKind == JsonValueKind.String
                && string.Equals(kind.GetString(), "ghpatch", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return KindSignature.IsMatch(text);
        }
    }
}
