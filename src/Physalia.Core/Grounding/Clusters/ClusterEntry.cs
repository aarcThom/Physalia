// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;

namespace Physalia.Core.Grounding.Clusters;

/// <summary>
/// One Grasshopper cluster available for grounding: its display name, the file it loads from, an
/// optional human-written description, and its introspected input/output parameter signature. The
/// signature is read once from the cluster file (in the Grasshopper layer) so that grounding and
/// placement stay pure functions with no Grasshopper dependency in <c>Physalia.Core</c>.
/// </summary>
/// <param name="Name">The cluster's display name (used by the model to reference it).</param>
/// <param name="FilePath">The absolute path to the cluster file.</param>
/// <param name="Description">A human-written description of what the cluster does, or an empty string.</param>
/// <param name="Inputs">The cluster's input parameters, in order.</param>
/// <param name="Outputs">The cluster's output parameters, in order.</param>
public sealed record ClusterEntry(
    string Name,
    string FilePath,
    string Description,
    IReadOnlyList<ClusterPort> Inputs,
    IReadOnlyList<ClusterPort> Outputs)
{
    /// <summary>
    /// Gets the cluster's inputs, never null.
    /// </summary>
    public IReadOnlyList<ClusterPort> Inputs { get; init; } = Inputs ?? Array.Empty<ClusterPort>();

    /// <summary>
    /// Gets the cluster's outputs, never null.
    /// </summary>
    public IReadOnlyList<ClusterPort> Outputs { get; init; } = Outputs ?? Array.Empty<ClusterPort>();
}
