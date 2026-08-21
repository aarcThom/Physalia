// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_Conversation"/> values between components.
/// </summary>
public class Param_Conversation : PhyParam<GH_Conversation>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_Conversation"/> class.
    /// </summary>
    public Param_Conversation()
        : base("Conversation", "Conv", "A conversation: its turns in order, oldest first. Adding a turn makes a new conversation rather than altering this one.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("EC70AAE8-B5C7-4947-A4EF-F3EF1A88E636");
}
