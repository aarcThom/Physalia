// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// A tool advertised to the model: its name, a description of when to use it, and a JSON Schema
/// describing the arguments the model must produce to call it. Providers serialise this into their
/// own wire format (Anthropic <c>input_schema</c>, OpenAI <c>function.parameters</c>,
/// Gemini <c>functionDeclarations.parameters</c>).
/// </summary>
/// <param name="Name">
/// The tool name the model emits when calling it. Must match the name a tool executor dispatches on.
/// </param>
/// <param name="Description">
/// A description of what the tool does and when to call it. The model relies on this to decide
/// whether and when to invoke the tool.
/// </param>
/// <param name="InputSchemaJson">
/// A JSON Schema object (as a string) describing the tool's arguments — typically
/// <c>{ "type": "object", "properties": { ... }, "required": [ ... ] }</c>. Blank or unparseable
/// schemas fall back to an empty object schema.
/// </param>
public record LlmToolDefinition(string Name, string Description, string InputSchemaJson);
