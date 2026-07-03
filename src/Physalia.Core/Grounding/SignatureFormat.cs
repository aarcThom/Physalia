// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

namespace Physalia.Core.Grounding;

/// <summary>
/// Shared <c>Name:Type</c> port formatting used by cluster and component signatures, so the two
/// groundings render ports identically and can never drift apart.
/// </summary>
public static class SignatureFormat
{
    /// <summary>
    /// Renders one port as <c>Name:Type</c>, or just <c>Name</c> when the type hint is blank.
    /// </summary>
    /// <param name="name">The port's short label.</param>
    /// <param name="typeHint">The port's type hint, or an empty string when unknown.</param>
    /// <returns>The formatted port text.</returns>
    public static string Port(string name, string typeHint) =>
        string.IsNullOrWhiteSpace(typeHint) ? name : $"{name}:{typeHint}";
}
