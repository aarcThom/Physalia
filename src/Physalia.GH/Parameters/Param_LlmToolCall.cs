// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_LlmToolCall"/> values between components.
/// </summary>
public class Param_LlmToolCall : GH_Param<GH_LlmToolCall>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_LlmToolCall"/> class.
    /// </summary>
    public Param_LlmToolCall()
        : base("LlmToolCall", "TC", "A tool call requested by the LLM.", "Physalia", "Params", GH_ParamAccess.item)
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("E9F0A1B2-C3D4-E5F6-A7B8-C9D0E1F2A3B4");

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}
