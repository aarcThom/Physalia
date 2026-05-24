// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Config;

/// <summary>
/// A resolved API key for a named provider.
/// </summary>
/// <param name="Provider">
/// The provider name as it appears in the config file, e.g. "anthropic", "openai", "gemini".
/// </param>
/// <param name="Key">The resolved API key value.</param>
public record ApiKey(string Provider, string Key);
