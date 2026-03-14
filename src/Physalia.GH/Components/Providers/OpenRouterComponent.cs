// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The OpenRouter API component.
    /// </summary>
    public class OpenRouterComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenRouterComponent"/> class.
        /// </summary>
        public OpenRouterComponent()
          : base(
                "OpenRouter",
                "OpenRouter",
                "Models via the OpenRouter API",
                "F6A7B8C9-D0E1-2345-F012-456789012345",
                "openrouter")
        {
        }
    }
}
