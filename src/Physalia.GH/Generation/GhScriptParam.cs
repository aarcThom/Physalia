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

/// <summary>
/// A request to create one input or output parameter on a GH Python Script component,
/// carrying an optional type hint. Used when pushing LLM-generated parameters.
/// </summary>
/// <param name="Name">Variable name used inside the script (e.g. <c>x</c>).</param>
/// <param name="TypeHint">
/// Physalia type-hint name (e.g. <c>Number</c>, <c>Point</c>, <c>Curve</c>). Empty or
/// unrecognised hints fall back to an untyped (any) parameter.
/// </param>
/// <param name="Access">Data access mode.</param>
public record GhParamSpec(
    string Name,
    string TypeHint,
    GhScriptParamAccess Access);

/// <summary>
/// What is actually arriving on a connected input of a script component, read off its volatile
/// data rather than its declaration — the two disagree whenever the user has left the type hint
/// or access unset, which is the common case.
/// </summary>
/// <param name="Count">How many items arrive in total.</param>
/// <param name="Branches">How many data-tree branches they arrive in.</param>
/// <param name="TypeName">What they are (e.g. <c>Curve</c>), or empty when nothing has flowed yet.</param>
public record GhIncomingData(int Count, int Branches, string TypeName);
