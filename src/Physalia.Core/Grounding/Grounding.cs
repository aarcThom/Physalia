// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Physalia.Core.Grounding.Components;

namespace Physalia.Core.Grounding;

/// <summary>
/// Model-grounding context folded into the system prompt. A discriminated union (closed set of
/// records) so that every kind of grounding — an installed-component catalog, a Grasshopper
/// cluster, a python function — grounds the model the same way: by contributing a labelled
/// section of text. New grounding kinds are added by writing one record that overrides
/// <see cref="ToSystemPromptSection"/>; there is no central switch to edit.
/// </summary>
public abstract record Grounding
{
    /// <summary>
    /// Renders this grounding as a self-contained section to append to the system prompt.
    /// </summary>
    /// <returns>
    /// The section text, or an empty/whitespace string to contribute nothing (the assembler
    /// drops empty sections).
    /// </returns>
    public abstract string ToSystemPromptSection();
}

/// <summary>
/// Grounds the model with the names of the components installed in the user's Grasshopper, so it
/// favours names that actually exist. Wraps the live <see cref="ComponentCatalog"/> snapshot.
/// </summary>
/// <param name="Catalog">The installed-component catalog.</param>
public sealed record ComponentCatalogGrounding(ComponentCatalog Catalog) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (Catalog is null || Catalog.Count == 0)
        {
            return string.Empty;
        }

        return "These Grasshopper components are installed and available. Use these exact names where one fits:\n"
            + string.Join(", ", Catalog.ComponentNames);
    }
}

/// <summary>
/// Grounds the model with a Grasshopper cluster (.ghx) available for use.
/// </summary>
/// <param name="Name">The cluster's display name.</param>
/// <param name="Description">A description of what the cluster does and its inputs/outputs.</param>
/// <remarks>
/// Scaffold: today this carries only a name and a free-text description. TODO: extract the
/// cluster's input/output parameter specs from the .ghx and render them as a structured signature.
/// </remarks>
public sealed record ClusterGrounding(string Name, string Description) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return string.Empty;
        }

        string body = string.IsNullOrWhiteSpace(Description) ? string.Empty : "\n" + Description.Trim();
        return $"The following Grasshopper cluster is available — use it where it fits: {Name.Trim()}." + body;
    }
}

/// <summary>
/// Grounds the model with a python function available for use.
/// </summary>
/// <param name="Signature">The function signature (e.g. <c>def foo(a, b) -> float</c>).</param>
/// <param name="Docstring">A description of what the function does.</param>
/// <remarks>
/// Scaffold: today this carries a free-text signature and docstring. TODO: parse the real
/// signature and docstring from the function source.
/// </remarks>
public sealed record PythonFunctionGrounding(string Signature, string Docstring) : Grounding
{
    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (string.IsNullOrWhiteSpace(Signature))
        {
            return string.Empty;
        }

        string body = string.IsNullOrWhiteSpace(Docstring) ? string.Empty : "\n" + Docstring.Trim();
        return $"The following python function is available — use it where it fits:\n{Signature.Trim()}" + body;
    }
}
