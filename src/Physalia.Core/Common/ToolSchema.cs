// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;

namespace Physalia.Core.Common;

/// <summary>
/// The single reading of a tool's declared argument schema, shared by every provider that has to
/// put one on the wire — the HTTP protocols through <c>ProtocolProviderBase.ParseToolSchema</c>,
/// and the Codex CLI's <c>dynamicTools</c>. Kept in one place because the fallback is a
/// correctness decision, not a formatting one: a tool whose schema will not parse must still be
/// callable, or a single malformed tool node takes the whole round down with it.
/// </summary>
public static class ToolSchema
{
    /// <summary>
    /// Parses a tool's JSON Schema, falling back to a minimal empty-object schema when the text is
    /// blank or will not parse — which is what <see cref="LlmToolDefinition.InputSchemaJson"/>
    /// promises callers.
    /// </summary>
    /// <param name="schemaJson">The declared schema as JSON text; may be null, blank or invalid.</param>
    /// <returns>The parsed schema, or an empty object schema.</returns>
    public static JsonNode Parse(string? schemaJson)
    {
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                JsonNode? parsed = JsonNode.Parse(schemaJson);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall through to the minimal object schema below.
            }
        }

        return new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
    }
}
