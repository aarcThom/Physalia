// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Physalia.Core.Config;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves a web-tool API key (Tavily, Brave, Jina) from <c>Files/API_KEY_CONFIG.YAML</c> using the
/// same env-var-then-file resolution as the LLM keys. Keys live under the <c>web_search</c> section;
/// each leaf name (e.g. <c>tavily</c>) becomes the provider id returned by <see cref="Api.GetKeys"/>.
/// Keys are never serialized — they are read from the config at solve time only.
/// </summary>
internal static class WebToolKeys
{
    /// <summary>
    /// Returns the resolved key for the given web-tool provider, or null when none is configured.
    /// </summary>
    /// <param name="provider">The provider leaf name, e.g. "tavily" or "jina".</param>
    /// <returns>The resolved key, or null.</returns>
    internal static string? Resolve(string provider)
    {
        string? key = Api.GetKeys(ConfigFilePath())
            .FirstOrDefault(k => string.Equals(k.Provider, provider, StringComparison.OrdinalIgnoreCase))
            ?.Key;

        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    private static string ConfigFilePath()
    {
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return assemblyDir is null
            ? "API_KEY_CONFIG.YAML"
            : Path.Combine(assemblyDir, "Files", "API_KEY_CONFIG.YAML");
    }
}
