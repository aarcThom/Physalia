using Grasshopper.Kernel;
using Physalia.GH.Goo;
using System;

namespace Physalia.GH.ParamTypes;

public class Param_LlmConfig : GH_Param<GH_LlmConfig>
{
    public Param_LlmConfig()
        : base("LlmConfig", "LLM", "LLM provider, model, and API key",
               "Physalia", "Core", GH_ParamAccess.item)
    { }

    public override Guid ComponentGuid => new Guid("DF372577-B98E-4440-9CD5-4001992324C0");
    protected override System.Drawing.Bitmap Icon => null;
}