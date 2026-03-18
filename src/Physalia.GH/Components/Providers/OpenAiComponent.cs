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
                "CA62D7B2-2039-4FA2-884D-250B08A36D03",
                "openai")
        {
        }
    }
}
