// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The Ollama local model component.
    /// </summary>
    public class OllamaComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OllamaComponent"/> class.
        /// </summary>
        public OllamaComponent()
          : base(
                "Ollama",
                "Ollama",
                "Local Models via Ollama",
                "D4E5F6A7-B8C9-0123-DEF0-234567890123",
                "ollama")
        {
        }
    }
}
