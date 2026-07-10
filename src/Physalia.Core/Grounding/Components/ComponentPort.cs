// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

namespace Physalia.Core.Grounding.Components;

/// <summary>
/// How a Grasshopper parameter consumes its collected data. Mirrors GH_ParamAccess without a
/// Grasshopper dependency (this is a Core type). An item-access input that collects more than one
/// wire builds a list and multiplies every downstream item — the classic silent authoring bug the
/// wiring lint flags; list/tree-access inputs take multiple wires idiomatically.
/// </summary>
public enum PortAccess
{
    /// <summary>Access is unknown (introspection unavailable, or the variable-parameter sentinel).</summary>
    Unknown,

    /// <summary>The parameter consumes one item per solve iteration.</summary>
    Item,

    /// <summary>The parameter consumes a list per branch.</summary>
    List,

    /// <summary>The parameter consumes the whole tree.</summary>
    Tree,
}

/// <summary>
/// One input or output parameter of a Grasshopper component: the parameter's full Name (the label
/// the model must author in <c>inputSettings.parameterName</c>, e.g. <c>Closed</c>) plus a short
/// type hint (the Grasshopper type name, e.g. <c>Vector</c>). The hint lets the model supply
/// correctly typed data without placing the component first.
/// </summary>
/// <param name="Name">The parameter's full Name (falling back to the nickname when blank).</param>
/// <param name="TypeHint">A short type name for the parameter, or an empty string when unknown.</param>
/// <param name="Required">
/// True for an input with no built-in default value that is not marked optional — leaving it
/// unwired (and un-internalized) yields nulls or no output downstream. Always false for outputs.
/// </param>
/// <param name="Access">
/// How the parameter consumes its collected data (item/list/tree), driving the multi-wire lint.
/// </param>
public sealed record ComponentPort(string Name, string TypeHint, bool Required = false, PortAccess Access = PortAccess.Unknown);
