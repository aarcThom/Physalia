// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The Anthropic API component.
    /// </summary>
    public class AnthropicComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AnthropicComponent"/> class.
        /// </summary>
        public AnthropicComponent()
          : base(
                "Anthropic API",
                "Claude API",
                "Anthropic Models via the Anthropic API",
                "54D82BF8-61D7-4242-89F5-4557CF61E82B",
                "anthropic")
        {
        }
    }
}