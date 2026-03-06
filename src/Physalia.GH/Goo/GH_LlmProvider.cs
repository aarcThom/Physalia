// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;
using Physalia.Core.Providers;

namespace Physalia.GH.Goo;

public class GH_LlmProvider : GH_Goo<LlmProvider>
{
    public GH_LlmProvider() { }
    public GH_LlmProvider(LlmProvider provider) { Value = provider; }

    public override bool IsValid => Value != null;
    public override string TypeName => "LlmProvider";
    public override string TypeDescription => "LLM provider and selected model";
    public override IGH_Goo Duplicate() => new GH_LlmProvider(Value);
    public override string ToString() => Value == null ? "null" : $"{Value.ProviderName} / {Value.CurrentModel}";

    public override bool CastTo<Q>(ref Q target) => false;
    public override bool CastFrom(object source) => false;

    // Don't persist the API key to disk — leave Write/Read as stubs
    public override bool Write(GH_IO.Serialization.GH_IWriter writer) => true;
    public override bool Read(GH_IO.Serialization.GH_IReader reader) => true;
}