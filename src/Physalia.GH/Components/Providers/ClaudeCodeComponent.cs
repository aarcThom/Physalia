// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The Claude Code API component.
    /// </summary>
    public class ClaudeCodeComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClaudeCodeComponent"/> class.
        /// </summary>
        public ClaudeCodeComponent()
          : base(
                "Claude Code",
                "CC",
                "Models via Claude Code",
                "C9D0E1F2-A3B4-5678-2345-789012345678",
                "claude code")
        {
        }
    }
}
