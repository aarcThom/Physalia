using Grasshopper.Kernel.Types;
using Physalia.Core.Config;

namespace Physalia.GH.Goo;

  public class GH_LlmConfig : GH_Goo<LlmConfig>
{
    public GH_LlmConfig() { }
    public GH_LlmConfig(LlmConfig config) { Value = config; }

    public override bool IsValid => Value != null;
    public override string TypeName => "LlmConfig";
    public override string TypeDescription => "LLM provider, model, and API key";
    public override IGH_Goo Duplicate() => new GH_LlmConfig(Value);
    public override string ToString() => Value == null ? "null" : $"{Value.Provider} / {Value.ModelId}";

    public override bool CastTo<Q>(ref Q target) => false;
    public override bool CastFrom(object source) => false;

    // Don't persist the API key to disk — leave Write/Read as stubs
    public override bool Write(GH_IO.Serialization.GH_IWriter writer) => true;
    public override bool Read(GH_IO.Serialization.GH_IReader reader) => true;
}