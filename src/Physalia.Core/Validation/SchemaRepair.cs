// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Physalia.Core.Validation;

/// <summary>
/// Repairs the schema violations that carry no information — the ones where bouncing the document
/// back teaches the model nothing and costs a full resubmission.
///
/// <para>Only additive defects qualify. A property the schema does not allow is one the model
/// invented alongside an otherwise conformant document; deleting it yields exactly the document
/// the model meant to send. Nothing here can change what gets placed: a repair either produces a
/// document that validates cleanly or is abandoned, and the caller re-validates before trusting
/// it.</para>
///
/// <para>The cost this avoids is not hypothetical. Observed in a live session: two rounds burned
/// on stray <c>groups_note_placeholder</c> / <c>groups_modify_placeholder</c> keys, each round
/// re-sending a ~12,000-character patch verbatim to delete one line, because the feedback's only
/// available instruction was "resubmit your ENTIRE response".</para>
/// </summary>
public static class SchemaRepair
{
    /// <summary>
    /// Deletes every property named by a <see cref="SchemaViolationKind.DisallowedProperty"/>
    /// violation.
    /// </summary>
    /// <param name="json">The instance document.</param>
    /// <param name="violations">The violations reported against it.</param>
    /// <returns>
    /// The repaired JSON and the pointers actually removed, or null when there was nothing safely
    /// repairable — no qualifying violations, a violation of another kind present (repairing part
    /// of a broken document would send back something the model never wrote), or a parse failure.
    /// </returns>
    public static RepairOutcome? DropDisallowedProperties(string json, IReadOnlyList<SchemaViolation> violations)
    {
        if (violations is null || violations.Count == 0)
        {
            return null;
        }

        // Every violation must be repairable. A document with a real defect alongside a stray key
        // has to go back regardless, and silently dropping the stray one first would only make the
        // returned feedback describe a document the model does not have.
        if (violations.Any(v => v.Kind != SchemaViolationKind.DisallowedProperty))
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception)
        {
            return null;
        }

        if (root is null)
        {
            return null;
        }

        var removed = new List<string>();
        foreach (SchemaViolation violation in violations)
        {
            if (TryRemove(root, violation.Path))
            {
                removed.Add(violation.Path);
            }
        }

        return removed.Count == 0 ? null : new RepairOutcome(root.ToJsonString(), removed);
    }

    // Walks a JSON Pointer to the parent of the target and removes the final segment. Object
    // members only — an array element is positional, and deleting one silently renumbers every
    // element after it.
    private static bool TryRemove(JsonNode root, string pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '/')
        {
            return false;
        }

        string[] segments = pointer[1..].Split('/').Select(Unescape).ToArray();
        if (segments.Length == 0)
        {
            return false;
        }

        JsonNode? node = root;
        for (int i = 0; i < segments.Length - 1 && node is not null; i++)
        {
            node = node switch
            {
                JsonObject obj => obj.TryGetPropertyValue(segments[i], out JsonNode? child) ? child : null,
                JsonArray arr => int.TryParse(segments[i], out int index) && index >= 0 && index < arr.Count
                    ? arr[index]
                    : null,
                _ => null,
            };
        }

        return node is JsonObject parent && parent.Remove(segments[^1]);
    }

    // RFC 6901: "~1" is a literal '/', "~0" a literal '~'. Order matters — unescaping '~0' first
    // would turn the encoded "~01" into "~1" and then into '/'.
    private static string Unescape(string segment) => segment.Replace("~1", "/").Replace("~0", "~");
}

/// <summary>
/// The result of a successful repair.
/// </summary>
/// <param name="Json">The repaired document. The caller must re-validate before trusting it.</param>
/// <param name="RemovedPaths">JSON Pointers to the properties that were deleted.</param>
public record RepairOutcome(string Json, IReadOnlyList<string> RemovedPaths);
