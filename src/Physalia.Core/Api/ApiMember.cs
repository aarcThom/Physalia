// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;

namespace Physalia.Core.Api;

/// <summary>
/// The kind of API member a <see cref="ApiMember"/> describes.
/// </summary>
public enum ApiMemberKind
{
    /// <summary>A type (class, struct, interface, enum, or delegate).</summary>
    Type,

    /// <summary>An instance constructor.</summary>
    Constructor,

    /// <summary>A method.</summary>
    Method,

    /// <summary>A property (including indexers).</summary>
    Property,

    /// <summary>A field, constant, or enum value.</summary>
    Field,

    /// <summary>An event.</summary>
    Event,
}

/// <summary>
/// One documented parameter of an API member: its name and the prose description harvested from
/// the XML documentation (empty when the member was undocumented).
/// </summary>
/// <param name="Name">The parameter name as declared.</param>
/// <param name="Description">The parameter's documentation text, or an empty string.</param>
public sealed record ApiParamDoc(string Name, string Description);

/// <summary>
/// One retrievable member of a .NET API surface (e.g. RhinoCommon): the authoritative signature
/// derived from reflection, enriched with the human description merged in from the assembly's XML
/// documentation file. This is the unit a code-generating model retrieves to write a compilable call.
///
/// <para>The signature carries exactly what reflection knows and the XML omits — return type,
/// static/instance, parameter types and names — while <see cref="Summary"/>, <see cref="Returns"/>,
/// and <see cref="Parameters"/> carry the prose the XML knows and reflection omits.</para>
/// </summary>
/// <param name="Kind">The kind of member.</param>
/// <param name="DeclaringType">The full display name of the declaring type, e.g. <c>Rhino.Geometry.Brep</c>.</param>
/// <param name="MemberName">The member's simple name (the type's short name for type/constructor entries).</param>
/// <param name="Signature">The full C#-style signature line (return type, modifiers, parameters).</param>
/// <param name="IsStatic">True when the member is static.</param>
/// <param name="Summary">The member's summary documentation, or an empty string.</param>
/// <param name="Returns">The member's returns documentation, or an empty string.</param>
/// <param name="Parameters">Per-parameter documentation, or an empty list.</param>
public sealed record ApiMember(
    ApiMemberKind Kind,
    string DeclaringType,
    string MemberName,
    string Signature,
    bool IsStatic,
    string Summary,
    string Returns,
    IReadOnlyList<ApiParamDoc> Parameters)
{
    private string? _nameLower;
    private string? _typeTailLower;
    private string? _summaryLower;

    /// <summary>
    /// Gets the lower-cased member name, cached for repeated search scoring.
    /// </summary>
    public string NameLower => _nameLower ??= MemberName.ToLowerInvariant();

    /// <summary>
    /// Gets the lower-cased final segment of the declaring type name (e.g. <c>brep</c>), cached for
    /// repeated search scoring.
    /// </summary>
    public string TypeTailLower => _typeTailLower ??= TypeTail(DeclaringType).ToLowerInvariant();

    /// <summary>
    /// Gets the lower-cased summary text, cached for repeated search scoring.
    /// </summary>
    public string SummaryLower => _summaryLower ??= Summary.ToLowerInvariant();

    private static string TypeTail(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
    }
}
