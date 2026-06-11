// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_ConversationMessage"/> values between components.
/// </summary>
public class Param_ConversationMessage : PhyParam<GH_ConversationMessage>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ConversationMessage"/> class.
    /// </summary>
    public Param_ConversationMessage()
        : base("ConversationMessage", "Msg", "A single turn in a conversation.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("33D8112F-A54F-482B-934F-8D994576DABA");
}
