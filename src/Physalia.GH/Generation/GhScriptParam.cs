// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Generation;

/// <summary>
/// Data access mode for a GH Python Script parameter.
/// Mirrors <c>RhinoCodePlatform.GH.ScriptParamAccess</c> without exposing it.
/// </summary>
public enum GhScriptParamAccess
{
    /// <summary>Single item per branch.</summary>
    Item,

    /// <summary>Full list per branch.</summary>
    List,

    /// <summary>Full data tree.</summary>
    Tree,
}

/// <summary>
/// Physalia's representation of one input or output parameter on a GH Python Script component.
/// </summary>
/// <param name="Name">Variable name used inside the script (e.g. <c>x</c>).</param>
/// <param name="PrettyName">Display name shown on the component (e.g. <c>X</c>).</param>
/// <param name="Description">Tooltip description.</param>
/// <param name="Access">Data access mode.</param>
public record GhScriptParam(
    string Name,
    string PrettyName,
    string Description,
    GhScriptParamAccess Access);
