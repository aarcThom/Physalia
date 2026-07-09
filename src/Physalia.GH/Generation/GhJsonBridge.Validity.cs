// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Linq;
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
            if (!validation.IsValid)
            {
                message = string.Join("\n", validation.Errors.Select(e => e.ToString()));
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
}
