// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_ImageSource"/> values between components.
/// </summary>
public class Param_ImageSource : PhyParam<GH_ImageSource>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ImageSource"/> class.
    /// </summary>
    public Param_ImageSource()
        : base("Image Source", "Img", "An image plus its alias, ready for inline delivery to a multimodal model.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A4F2C7E1-3B98-4D6A-9E25-7C1B0F8A2D34");
}
