// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The Groq API component.
    /// </summary>
    public class GroqComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GroqComponent"/> class.
        /// </summary>
        public GroqComponent()
          : base(
                "Groq",
                "Groq",
                "Models via the Groq API",
                "E5F6A7B8-C9D0-1234-EF01-345678901234",
                "groq")
        {
        }
    }
}
