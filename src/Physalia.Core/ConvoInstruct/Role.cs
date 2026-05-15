// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// The role of a participant in a conversation turn.
/// </summary>
public enum Role
{
    /// <summary>Human turn.</summary>
    User,

    /// <summary>Model turn.</summary>
    Assistant,
}
