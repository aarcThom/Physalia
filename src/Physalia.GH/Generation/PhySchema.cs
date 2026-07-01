// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.GH.Generation;

/// <summary>
/// A Grasshopper definition as emitted by the LLM, mirroring <c>PhySchema.json</c>.
/// Carries components and connections but no canvas positions; positions are
/// synthesised by <see cref="HierarchicalLayout"/>.
/// </summary>
/// <param name="Schema">The PhySchema version string (e.g. "1.0").</param>
/// <param name="Components">All components in the definition.</param>
/// <param name="Connections">All wires between component parameters.</param>
public sealed record PhySchemaDocument(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("components")] IReadOnlyList<PhySchemaComponent> Components,
    [property: JsonPropertyName("connections")] IReadOnlyList<PhySchemaConnection> Connections)
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Deserialises a validated PhySchema JSON string into a document model.
    /// </summary>
    /// <param name="json">The PhySchema JSON string (e.g. the Auditor's validated output).</param>
    /// <returns>The parsed document, or null if deserialisation yields no object.</returns>
    public static PhySchemaDocument? FromJson(string json)
    {
        return JsonSerializer.Deserialize<PhySchemaDocument>(json, DeserializeOptions);
    }
}

/// <summary>
/// A single Grasshopper component or floating parameter in a PhySchema document.
/// </summary>
/// <param name="Name">The component display name (e.g. "Addition").</param>
/// <param name="InstanceGuid">A UUID string unique within the definition.</param>
/// <param name="Id">Short integer identifier referenced by connections.</param>
/// <param name="ComponentState">Optional opaque extra state; ignored by layout.</param>
/// <param name="NickName">
/// Optional canvas label describing what this component is for — most useful on input sources such
/// as a Number Slider (e.g. "Radius", "Height"), so the placed definition reads clearly. Null/blank
/// leaves the component's default label.
/// </param>
public sealed record PhySchemaComponent(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("instanceGuid")] string InstanceGuid,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("componentState")] JsonElement? ComponentState = null,
    [property: JsonPropertyName("nickName")] string? NickName = null);

/// <summary>
/// A wire between two component parameters.
/// </summary>
/// <param name="From">The source endpoint (output parameter).</param>
/// <param name="To">The target endpoint (input parameter).</param>
public sealed record PhySchemaConnection(
    [property: JsonPropertyName("from")] PhySchemaEndpoint From,
    [property: JsonPropertyName("to")] PhySchemaEndpoint To);

/// <summary>
/// One end of a connection, referencing a component parameter.
/// </summary>
/// <param name="Id">The integer id of the component.</param>
/// <param name="ParamName">The parameter name on the component.</param>
public sealed record PhySchemaEndpoint(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("paramName")] string ParamName);
