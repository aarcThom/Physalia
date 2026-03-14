// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The Google Gemini API component.
    /// </summary>
    public class GeminiComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GeminiComponent"/> class.
        /// </summary>
        public GeminiComponent()
          : base(
                "Google AI",
                "Gemini",
                "Google Models via the Gemini API",
                "A1B2C3D4-E5F6-7890-ABCD-EF1234567890",
                "google")
        {
        }
    }
}
