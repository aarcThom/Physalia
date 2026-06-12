// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace Physalia.GH.Parameters;

/// <summary>
/// Base for the hidden Physalia parameters that carry goo values between components.
/// Owns the standard category ("Physalia"/"Params"), item access, and hidden exposure;
/// subclasses supply their identity strings and ComponentGuid.
/// </summary>
/// <typeparam name="TGoo">The goo type this parameter carries.</typeparam>
public abstract class PhyParam<TGoo> : GH_Param<TGoo>
    where TGoo : class, IGH_Goo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PhyParam{TGoo}"/> class.
    /// </summary>
    /// <param name="name">Parameter display name.</param>
    /// <param name="nickname">Parameter nickname.</param>
    /// <param name="description">Parameter description.</param>
    protected PhyParam(string name, string nickname, string description)
        : base(name, nickname, description, "Physalia", "Params", GH_ParamAccess.item)
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}
