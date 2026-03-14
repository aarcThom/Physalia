// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The DeepSeek API component.
    /// </summary>
    public class DeepSeekComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeepSeekComponent"/> class.
        /// </summary>
        public DeepSeekComponent()
          : base(
                "DeepSeek",
                "DeepSeek",
                "DeepSeek Models via the DeepSeek API",
                "C3D4E5F6-A7B8-9012-CDEF-123456789012",
                "deepseek")
        {
        }
    }
}
