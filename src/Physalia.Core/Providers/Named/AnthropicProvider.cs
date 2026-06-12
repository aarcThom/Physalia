// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Providers.Anthropic;

namespace Physalia.Core.Providers.Named;

/// <summary>
/// Provider for the Anthropic API. Use with <see cref="Physalia.Core.Models.Named.AnthropicConfig"/>.
/// </summary>
public sealed class AnthropicProvider : AnthropicProtocolProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnthropicProvider"/> class.
    /// </summary>
    public AnthropicProvider()
    {
    }
}
