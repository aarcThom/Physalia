// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The OpenAI API component.
    /// </summary>
    public class OpenAiComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenAiComponent"/> class.
        /// </summary>
        public OpenAiComponent()
          : base(
                "OpenAI",
                "GPT",
                "OpenAI Models via the OpenAI API",
                "B2C3D4E5-F6A7-8901-BCDE-F12345678901",
                "openai")
        {
        }
    }
}
