// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace Physalia.Core.Parsing;

/// <summary>
/// Represents the JSON response returned by the LLM, containing the generated Python script
/// and the input and output parameter definitions for the Grasshopper component.
/// </summary>
public class ScriptResponse
{
    /// <summary>
    /// Gets or sets the generated Python 3 script body.
    /// </summary>
    [JsonPropertyName("script")]
    public string Script { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of input parameter definitions derived from the LLM response.
    /// </summary>
    [JsonPropertyName("inputs")]
    public List<ParamDefinition> Inputs { get; set; } = new ();

    /// <summary>
    /// Gets or sets the list of output parameter definitions derived from the LLM response.
    /// </summary>
    [JsonPropertyName("outputs")]
    public List<ParamDefinition> Outputs { get; set; } = new ();
}

/// <summary>
/// Describes a single input or output parameter for the dynamically generated Grasshopper component.
/// </summary>
public class ParamDefinition
{
    /// <summary>
    /// Gets or sets the variable name used in the generated Python script.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable display name shown on the Grasshopper component.
    /// </summary>
    [JsonPropertyName("prettyName")]
    public string? PrettyName { get; set; }

    /// <summary>
    /// Gets or sets the tooltip shown when the user hovers over the parameter.
    /// </summary>
    [JsonPropertyName("tooltip")]
    public string? Tooltip { get; set; }

    /// <summary>
    /// Gets or sets the Grasshopper type hint string used to select the correct parameter type.
    /// </summary>
    [JsonPropertyName("typeHint")]
    public string? TypeHint { get; set; }

    /// <summary>
    /// Gets or sets the access mode for this parameter: "item", "list", or "tree".
    /// </summary>
    [JsonPropertyName("access")]
    public string Access { get; set; } = "item";

    /// <summary>
    /// Gets or sets a value indicating whether this parameter is optional.
    /// </summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;
}
