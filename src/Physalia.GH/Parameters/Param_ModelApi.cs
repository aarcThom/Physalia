// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_ModelApi"/> values between components.
/// </summary>
/// <remarks>
/// Replaces <c>Param_ApiKey</c> and takes a NEW ComponentGuid: the type it carries changed shape
/// (an endpoint joined the key), and the model components it feeds changed their parameter layout
/// in the same move, so there is no archive worth pretending to be compatible with.
/// </remarks>
public class Param_ModelApi : PhyParam<GH_ModelApi>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ModelApi"/> class.
    /// </summary>
    public Param_ModelApi()
        : base("Model API", "API", "A provider's endpoint and key, carried together as a label only. Neither is shown on the canvas or saved into your file.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("2C9F4E17-8B36-4A5D-9F02-6D41A7C5E8B3");
}
