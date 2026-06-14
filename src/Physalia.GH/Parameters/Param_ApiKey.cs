// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_ApiKey"/> values between components.
/// </summary>
public class Param_ApiKey : PhyParam<GH_ApiKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ApiKey"/> class.
    /// </summary>
    public Param_ApiKey()
        : base("API Key", "K", "A resolved API key. Displays as a label only; the secret is never shown or serialised.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B7E3D2A9-5C41-4F88-A1D6-9E2F7B3C8A04");
}
