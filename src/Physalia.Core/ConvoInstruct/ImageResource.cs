// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// An image source paired with a user-facing alias, so downstream consumers can
/// label or reference the image without re-deriving a name. The alias travels as
/// real data alongside the source, not merely as a display string.
/// </summary>
/// <param name="Alias">User-entered short name for the image.</param>
/// <param name="Source">The underlying image source (currently always an InlineImage).</param>
public record ImageResource(string Alias, ImageSource Source);
