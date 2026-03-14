// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.GH.Components.Providers
{
    /// <summary>
    /// The GitHub Models API component.
    /// </summary>
    public class GitHubModelsComponent : ProviderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GitHubModelsComponent"/> class.
        /// </summary>
        public GitHubModelsComponent()
          : base(
                "GitHub Models",
                "GH Models",
                "Models via GitHub Models",
                "A7B8C9D0-E1F2-3456-0123-567890123456",
                "github models")
        {
        }
    }
}
