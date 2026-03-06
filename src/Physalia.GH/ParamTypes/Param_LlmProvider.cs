// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.GH.Goo;

namespace Physalia.GH.ParamTypes;

public class Param_LlmProvider : GH_Param<GH_LlmProvider>
{
    public Param_LlmProvider()
        : base("LlmProvider", "LLM", "LLM provider and selected model",
               "Physalia", "Core", GH_ParamAccess.item)
    { }

    public override Guid ComponentGuid => new Guid("DF372577-B98E-4440-9CD5-4001992324C0");
    protected override System.Drawing.Bitmap Icon => null;
}