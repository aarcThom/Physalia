// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;

namespace Physalia.GH.ParamTypes;

public class LlmProviderGhParam : GH_Param<LlmProviderGoo>
{
    public LlmProviderGhParam()
        : base("LlmProvider", "LLM", "LLM provider and selected model",
               "Physalia", "Core", GH_ParamAccess.item)
    { }

    public override Guid ComponentGuid => new Guid("DF372577-B98E-4440-9CD5-4001992324C0");
    protected override System.Drawing.Bitmap Icon => null;

    public override GH_Exposure Exposure => GH_Exposure.hidden;
}