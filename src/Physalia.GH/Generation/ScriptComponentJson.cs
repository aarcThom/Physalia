// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Text.Json;
using Physalia.Core.Common;

namespace Physalia.GH.Generation;

/// <summary>
/// Parses the <c>{ code, inputs, outputs }</c> submission every script transmitter receives —
/// the shape declared by the PythonComponent and CSharpComponent schemas — into source plus typed
/// parameter specs.
///
/// <para>The two schemas differ in what the code must look like, never in how the submission is
/// shaped, so the parse lives here once and each transmitter adds only its own language checks
/// (Python's list-access promotion, C#'s RunScript signature agreement).</para>
/// </summary>
public static class ScriptComponentJson
{
    /// <summary>
    /// Parses a script-component submission into code plus typed input/output specs.
    /// </summary>
    /// <param name="json">The JSON string carried by the consumed signal.</param>
    /// <param name="code">The parsed source.</param>
    /// <param name="inputs">The parsed input parameter specs.</param>
    /// <param name="outputs">The parsed output parameter specs.</param>
    /// <param name="error">A parse error message when the result is false.</param>
    /// <returns>true if the JSON parsed and contained non-empty code; otherwise false.</returns>
    public static bool TryParse(
        string json,
        out string code,
        out List<GhParamSpec> inputs,
        out List<GhParamSpec> outputs,
        out string error)
    {
        code = string.Empty;
        inputs = new List<GhParamSpec>();
        outputs = new List<GhParamSpec>();
        error = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Root JSON value must be an object.";
                return false;
            }

            if (root.TryGetProperty("code", out JsonElement codeEl) && codeEl.ValueKind == JsonValueKind.String)
                code = codeEl.GetString() ?? string.Empty;

            inputs = ParseParams(root, "inputs");
            outputs = ParseParams(root, "outputs");

            if (!StringHelpers.IsNonBlank(code))
            {
                error = "Missing or empty 'code' field.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Maps a JSON access string (<c>item</c>, <c>list</c>, <c>tree</c>) to the access enum.
    /// </summary>
    /// <param name="access">The access string; unknown values default to item.</param>
    /// <returns>The mapped access mode.</returns>
    public static GhScriptParamAccess MapAccess(string access) => access.ToLowerInvariant() switch
    {
        "list" => GhScriptParamAccess.List,
        "tree" => GhScriptParamAccess.Tree,
        _ => GhScriptParamAccess.Item,
    };

    /// <summary>
    /// Reads a JSON array of <c>{ name, type, access }</c> parameter objects into specs.
    /// Entries missing a non-blank name are skipped; type defaults to untyped and access to item.
    /// </summary>
    /// <param name="root">The root JSON object.</param>
    /// <param name="propertyName">The array property to read (e.g. "inputs").</param>
    /// <returns>The parsed parameter specs.</returns>
    private static List<GhParamSpec> ParseParams(JsonElement root, string propertyName)
    {
        var specs = new List<GhParamSpec>();

        if (!root.TryGetProperty(propertyName, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            return specs;

        foreach (JsonElement el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;

            if (!el.TryGetProperty("name", out JsonElement nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;

            string name = nameEl.GetString() ?? string.Empty;
            if (!StringHelpers.IsNonBlank(name))
                continue;

            string typeHint = el.TryGetProperty("type", out JsonElement typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString() ?? string.Empty
                : string.Empty;

            string access = el.TryGetProperty("access", out JsonElement accessEl) && accessEl.ValueKind == JsonValueKind.String
                ? accessEl.GetString() ?? "item"
                : "item";

            specs.Add(new GhParamSpec(name, typeHint, MapAccess(access)));
        }

        return specs;
    }
}
