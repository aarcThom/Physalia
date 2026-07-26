// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GhJSON.Core;
using GhJSON.Core.PatchModels;
using GhJSON.Core.Validation;
using Physalia.Core.Validation;

namespace Physalia.GH.Generation;

/// <summary>
/// Payload-integrity validation for the GH Definition Validator guardrail. This is the GhJSON
/// library's own three-layer verdict — parse, embedded JSON-schema conformance, and structural
/// integrity (unique ids, connection references resolve, group membership; for a ghpatch, the
/// patch schema plus no-instanceGuid-on-adds) — run as an explicit pipeline node instead of a
/// hidden pre-check inside placement. The Component Transmitter itself no longer pre-validates:
/// a payload that cannot even be parsed surfaces there as a genuine placement failure, while
/// everything statically knowable is this gate's job.
/// </summary>
internal static partial class GhJsonBridge
{
    /// <summary>
    /// Validates a payload as a full GhJSON document or a ghpatch, whichever it declares itself
    /// to be. Unlike the other guardrail façades, malformed JSON FAILS here — validity is this
    /// check's entire purpose, so there is nothing to defer to.
    /// </summary>
    /// <param name="json">The payload — a full GhJSON graph or a ghpatch document.</param>
    /// <param name="isPatch">Whether the payload declared itself a ghpatch (drives feedback wording).</param>
    /// <param name="message">The validation failure message, or null when valid.</param>
    /// <returns>true when the payload is valid; false otherwise.</returns>
    internal static bool ValidateDefinitionJson(string json, out bool isPatch, out string? message)
    {
        isPatch = GhPatchDetector.IsGhPatch(json);
        if (!isPatch)
        {
            return GhJson.IsValid(json, out message);
        }

        try
        {
            GhPatchDocument patch = GhJson.PatchFromJson(json);
            ValidationResult validation = GhJson.ValidatePatch(patch);
            List<string> errors = validation.Errors
                .Select(DescribePatchError)
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();

            if (errors.Count > 0)
            {
                message = string.Join("\n", errors);
                return false;
            }

            message = null;
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    // The library's patch schema declares an add as allOf[componentData/groupData, not{required:
    // ["instanceGuid"]}]. JsonSchema.Net reports the inner required-check's failure — which is
    // exactly what makes the enclosing `not` SUCCEED — and the library's error walker suppresses
    // only failing anyOf/oneOf branches, not failing `not` branches. So a CORRECT add (no
    // instanceGuid) is reported as 'Required properties ["instanceGuid"] are not present', and a
    // model that complies is then told it must not specify one: an unsatisfiable loop. Group adds
    // hit it twice over, because groupData's identity anyOf lists instanceGuid as one alternative
    // when the patch schema forbids it outright — leaving `id` as the only real requirement.
    // Neither the `not` nor the anyOf can ever be satisfied via instanceGuid, so a
    // missing-instanceGuid complaint against an add is never actionable: strip instanceGuid from
    // these required-property lists and drop the error when nothing else was missing.
    private static readonly Regex RequiredPropertiesPattern = new(
        @"^Required properties \[(?<props>.*?)\] are not present$",
        RegexOptions.Compiled);

    /// <summary>
    /// Renders one library validation error for the model, or returns null when the error is an
    /// artifact of the patch schema's inverted instanceGuid check rather than a real defect.
    /// </summary>
    /// <param name="error">The library's validation message.</param>
    /// <returns>The line to show the model, or null to suppress it.</returns>
    private static string? DescribePatchError(ValidationMessage error)
    {
        string path = error.Path ?? string.Empty;
        bool isSchemaCheckOnAdd =
            path.Contains("/add/", StringComparison.Ordinal)
            && (path.Contains("/components/", StringComparison.Ordinal)
                || path.Contains("/groups/", StringComparison.Ordinal));

        if (!isSchemaCheckOnAdd)
        {
            return error.ToString();
        }

        Match match = RequiredPropertiesPattern.Match(error.Message.Trim());
        if (!match.Success)
        {
            return error.ToString();
        }

        // Keep every missing property EXCEPT instanceGuid, which an add must never carry.
        List<string> remaining = match.Groups["props"].Value
            .Split(',')
            .Select(p => p.Trim().Trim('"'))
            .Where(p => p.Length > 0 && !p.Equals("instanceGuid", StringComparison.Ordinal))
            .ToList();

        if (remaining.Count == 0)
        {
            return null;
        }

        string quoted = string.Join(", ", remaining.Select(p => $"\"{p}\""));
        return $"{path}: Required properties [{quoted}] are not present "
            + "(an added component or group must NOT carry an instanceGuid — it is generated on placement).";
    }
}
