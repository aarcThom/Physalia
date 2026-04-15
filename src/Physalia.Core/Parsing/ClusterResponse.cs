// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Physalia.Core.Parsing;

/// <summary>
/// Represents the JSON response returned by the LLM for a cluster receiver,
/// describing the outer parameters and internal component graph.
/// </summary>
public class ClusterResponse
{
    /// <summary>
    /// Gets or sets a short human-readable summary of what the LLM built.
    /// </summary>
    [JsonPropertyName("statusMessage")]
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the outer input parameter definitions for the cluster.
    /// </summary>
    [JsonPropertyName("inputs")]
    public List<ParamDefinition> Inputs { get; set; } = new ();

    /// <summary>
    /// Gets or sets the outer output parameter definitions, each including
    /// the wire source within the inner document.
    /// </summary>
    [JsonPropertyName("outputs")]
    public List<ClusterOutputDef> Outputs { get; set; } = new ();

    /// <summary>
    /// Gets or sets the inner component definitions that make up the cluster graph.
    /// </summary>
    [JsonPropertyName("components")]
    public List<ClusterComponentDef> Components { get; set; } = new ();
}

/// <summary>
/// Describes a single cluster output parameter, including which inner component
/// output it should be wired from.
/// </summary>
public class ClusterOutputDef
{
    /// <summary>
    /// Gets or sets the parameter name used on the outer component.
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
    /// Gets or sets the wire source for this output, in the form
    /// <c>"componentId.nickName"</c> or <c>"input.paramName"</c>.
    /// </summary>
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;
}

/// <summary>
/// Describes a single Grasshopper component to be placed inside the cluster,
/// including its type, optional nickname, and input wire connections.
/// </summary>
public class ClusterComponentDef
{
    /// <summary>
    /// Gets or sets the unique identifier for this component within the cluster graph,
    /// used to reference it as a wire source (e.g. "c1").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the GH component name used to look it up via
    /// <c>GH_ComponentServer.FindObjectByName</c> (e.g. "Addition", "Move").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional display label for the component on the inner canvas.
    /// </summary>
    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    /// <summary>
    /// Gets or sets the input wire connections for this component, keyed by the
    /// input parameter NickName. Values are wire sources in the form
    /// <c>"input.paramName"</c> or <c>"componentId.outputNickName"</c>.
    /// </summary>
    [JsonPropertyName("inputs")]
    public Dictionary<string, string> Inputs { get; set; } = new ();

    /// <summary>
    /// Gets or sets the ID of the component this Panel annotates.
    /// Only used when <c>type</c> is "Panel". Null for all other component types.
    /// </summary>
    [JsonPropertyName("annotates")]
    public string? Annotates { get; set; }
}
