// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.GH.Config;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves a web-tool API key (Tavily, Jina) through the same resolver the model providers use —
/// environment variable first, then the encrypted credential store.
/// </summary>
/// <remarks>
/// Routed through <see cref="PhyCredentials"/> so the web tools and the Model API component can never
/// disagree about what is configured. Keys are never serialized — they are resolved at solve time
/// only.
/// </remarks>
internal static class WebToolKeys
{
    /// <summary>
    /// Returns the resolved key for the given web-tool provider, or null when none is configured.
    /// </summary>
    /// <param name="provider">The provider id, e.g. "tavily" or "jina".</param>
    /// <returns>The resolved key, or null.</returns>
    internal static string? Resolve(string provider)
    {
        string? key = PhyCredentials.Resolver.Resolve(provider)?.Key;
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }
}
