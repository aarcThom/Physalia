// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

namespace Physalia.Core.Grounding.Clusters;

/// <summary>
/// One input or output parameter of a Grasshopper cluster: the parameter's name plus a short
/// type hint (the Grasshopper type name, e.g. <c>Number</c>, <c>Point</c>, <c>Brep</c>). The hint
/// lets the model wire the cluster correctly without loading the file.
/// </summary>
/// <param name="Name">The parameter's name, exactly as the cluster exposes it.</param>
/// <param name="TypeHint">A short type name for the parameter, or an empty string when unknown.</param>
public sealed record ClusterPort(string Name, string TypeHint);
