// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

namespace Physalia.Core.Grounding.Components;

/// <summary>
/// One input or output parameter of a Grasshopper component: the parameter's short label (the
/// nickname shown on the canvas, e.g. <c>G</c>) plus a short type hint (the Grasshopper type
/// name, e.g. <c>Vector</c>). The hint lets the model supply correctly typed data without
/// placing the component first.
/// </summary>
/// <param name="Name">The parameter's short label (nickname, falling back to the full name).</param>
/// <param name="TypeHint">A short type name for the parameter, or an empty string when unknown.</param>
public sealed record ComponentPort(string Name, string TypeHint);
