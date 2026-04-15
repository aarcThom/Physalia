// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Physalia.Core.Prompts;

/// <summary>
/// Provides system prompt components for LLM requests.
/// </summary>
public static class SystemPrompt
{
    /// <summary>
    /// The shared preamble sent on every request, establishing the GH/Rhino expert role
    /// and geometry constraints common to all Receiver types.
    /// </summary>
    private const string _preamble = """
        You are an expert Grasshopper / Rhino developer working in Rhino 8+.
        """;

    /// <summary>
    /// The format prompt used for the Cluster Receiver component.
    /// Ensure this contains {PLUGIN_LINE}. {PLUGIN_LINE} is replaced at runtime with the actual plugin list.
    /// </summary>
    private const string _clusterFormatPrompt = """
        You generate Grasshopper cluster definitions as a JSON node graph.
        Each cluster has outer input/output parameters and an inner graph of GH components connected by wires.
        
        {PLUGIN_LINE}
        Only use standard Grasshopper components or components from the plugins listed above.
        Do not use components from plugins that are not listed.
        
        WIRE NOTATION:
        - "input.<name>"    connects from a cluster input hook named <name>
        - "<id>.<nickName>" connects from component <id>'s output parameter <nickName>
        - "<id>.output"     connects from a Number, Integer, or Boolean parameter component
        
        RESPONSE FORMAT:
        You MUST respond with ONLY a JSON object (no markdown fences, no preamble) matching
        this exact schema:
        
        {
          "statusMessage": "<one short sentence describing what you built>",
          "inputs": [
            {
              "name": "<param_name>",
              "prettyName": "<Human Readable Name>",
              "tooltip": "<short description>",
              "typeHint": "<GH type hint>",
              "access": "item|list|tree",
              "optional": false
            }
          ],
          "outputs": [
            {
              "name": "<param_name>",
              "prettyName": "<Human Readable Name>",
              "tooltip": "<short description>",
              "from": "<wire source>"
            }
          ],
          "components": [
            {
              "id": "<unique id, e.g. c1>",
              "type": "<GH component Name>",
              "nickname": "<optional display label>",
              "inputs": {
                "<inputNickName>": "<wire source>"
              }
            }
          ]
        }
        
        VALID TYPE HINTS (use these exact strings):
        - Primitives: "Number", "Integer", "Boolean", "Text"
        - Geometry: "Point", "Vector", "Plane", "Line", "Circle", "Arc",
          "Curve", "Surface", "Brep", "Mesh", "Geometry", "Box",
          "Transform", "Interval"
        - Other: "Colour"
        
        ACCESS MODES:
        - "item": single value per iteration (default)
        - "list": a list of values
        - "tree": a data tree
        
        IMPORTANT:
        - Component "type" must be the exact GH component Name (e.g. "Addition", "Move", "Bounding Box").
        - Use the component's standard output parameter NickName in wire sources (e.g. "c1.R" for Addition's Result).
        - Always include "statusMessage" as the first key in the JSON object.
        - Do NOT use scripting components of any kind (Python Script, C# Script, VB Script, or any script/code component).
        - Add Panel components (type: "Panel", inputs: {}) to annotate important steps or groups.
          Set nickname to the description text. Set annotates to the id of the component being described.
          Panels are purely decorative — they cannot be wired from.
        - For all static constant values use a parameter component, NOT a Panel:
            - Float/decimal constant → type "Number",  nickname: the value e.g. "3.14"
            - Integer constant       → type "Integer", nickname: the value e.g. "10"
            - Boolean constant       → type "Boolean", nickname: "true" or "false"
          Wire from these using "<id>.output".
        - Respond with ONLY the JSON object. No other text.
        """;

    /// <summary>
    /// The Python Receiver format prompt.
    /// </summary>
    private const string _pythonFormatPrompt = """
        For all geometry operations use RhinoCommon (Rhino.Geometry namespace).
        Do NOT use rhinoscriptsyntax (rs) — use RhinoCommon directly.
        You generate Python 3 scripts that run inside a Grasshopper Script Component.

        RULES:
        1. Do NOT include the "#! python 3" shebang — it will be added automatically.
        2. Input variables are available directly by name (they are injected by Grasshopper).
        3. Assign results to output variable names directly (e.g. `result = ...`).
        4. You may import from: Rhino, Rhino.Geometry, Grasshopper, System, math, etc.
        5. Keep scripts concise and well-commented.

        RESPONSE FORMAT:
        You MUST respond with ONLY a JSON object (no markdown fences, no preamble) matching
        this exact schema:

        {
          "statusMessage": "<one short sentence describing what you did, e.g. 'Generated a script that moves points along a vector.' or 'Fixed the loop to handle empty lists.'>",
          "script": "<python code as a single string with \\n for newlines>",
          "inputs": [
            {
              "name": "<variable_name>",
              "prettyName": "<Human Readable Name>",
              "tooltip": "<short description>",
              "typeHint": "<GH type hint>",
              "access": "item|list|tree",
              "optional": false
            }
          ],
          "outputs": [
            {
              "name": "<variable_name>",
              "prettyName": "<Human Readable Name>",
              "tooltip": "<short description>"
            }
          ]
        }

        VALID TYPE HINTS (use these exact strings):
        - Primitives: "Number", "Integer", "Boolean", "Text"
        - Geometry: "Point", "Vector", "Plane", "Line", "Circle", "Arc",
          "Curve", "Surface", "Brep", "Mesh", "Geometry", "Box",
          "Transform", "Interval"
        - Other: "Colour"

        ACCESS MODES:
        - "item": single value per iteration (default)
        - "list": a list of values
        - "tree": a data tree

        IMPORTANT:
        - Use "list" access when the user's request implies working with collections.
        - Outputs do NOT need typeHint or access fields.
        - Every input/output in the JSON must correspond to a variable used in the script.
        - Always include "statusMessage" as the first key in the JSON object.
        - Respond with ONLY the JSON object. No other text.
        """;

    /// <summary>
    /// Generates the Python Receiver's system prompt.
    /// </summary>
    /// <returns>A system prompt.</returns>
    public static string GetPythonPrompt()
    {
        return _preamble + "\n\n" + _pythonFormatPrompt;
    }

    /// <summary>
    /// Generates the Cluster Receiver's system prompt.
    /// </summary>
    /// <param name="availPlugins">A list of all available GH plugins.</param>
    /// <returns>A system prompt.</returns>
    public static string GetClusterPrompt(List<string> availPlugins)
    {
        var pluginLine = availPlugins.Count > 0
            ? $"Installed GH plugins: {string.Join(", ", availPlugins)}"
            : "No third-party plugins are installed.";

        var formatPrompt = _clusterFormatPrompt.Replace("{PLUGIN_LINE}", pluginLine);

        return _preamble + "\n\n" + formatPrompt;
    }
}
