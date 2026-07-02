// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Grounding.Tools;

/// <summary>
/// An immutable, opt-in selection of which tools (from a <see cref="ToolsGrounding"/>) are advertised
/// to the model. Keyed by tool name.
///
/// <para>A <see langword="null"/> selection (handled by the callers, never an instance of this class)
/// means "include every tool present on the canvas" — the default for a never-configured Recorder. An
/// instance with zero names means "include none". Unknown names (a tool since removed from the canvas)
/// are simply never matched, so a selection degrades gracefully as the document changes.</para>
/// </summary>
public sealed class ToolsSelection
{
    private readonly HashSet<string> _included;

    private ToolsSelection(HashSet<string> included)
    {
        _included = included;
    }

    /// <summary>
    /// Gets the included tool names, sorted for stable serialization.
    /// </summary>
    public IReadOnlyList<string> Names => _included
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Builds a selection from a flat sequence of tool names. Blank names are dropped.
    /// </summary>
    /// <param name="names">The included tool names.</param>
    /// <returns>A selection including exactly the supplied names.</returns>
    public static ToolsSelection FromNames(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                set.Add(name);
            }
        }

        return new ToolsSelection(set);
    }

    /// <summary>
    /// Returns whether the given tool name is included in this selection.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>True when the tool is included.</returns>
    public bool Includes(string name) =>
        !string.IsNullOrWhiteSpace(name) && _included.Contains(name);
}
