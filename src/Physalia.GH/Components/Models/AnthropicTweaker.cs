// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Models.Named;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Adjusts the inference parameters of an Anthropic model configuration.
/// </summary>
public class AnthropicTweaker : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnthropicTweaker"/> class.
    /// </summary>
    public AnthropicTweaker()
        : base("Anthropic Tweaker", "AnthTwk", "Adjusts temperature, top-p, and top-k on an Anthropic model configuration.", "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("ED550693-9492-482E-A70F-9BAD732B3C4F");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Anthropic model configuration to adjust.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Temperature", "t", "Sampling temperature (0.0–1.0). Clamped on intake.", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Top P", "p", "Nucleus sampling threshold (0.0–1.0).", GH_ParamAccess.item, 1.0);
        pManager.AddIntegerParameter("Top K", "k", "Top-K sampling pool size. Set to 0 to use the provider default.", GH_ParamAccess.item, 0);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Adjusted Anthropic model configuration.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var goo = new GH_ModelConfig();
        double temperature = 1.0;
        double topP = 1.0;
        int topK = 0;

        if (!DA.GetData(0, ref goo)) return;
        DA.GetData(1, ref temperature);
        DA.GetData(2, ref topP);
        DA.GetData(3, ref topK);

        if (goo.Value is not AnthropicConfig existing)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Input model must be an Anthropic configuration.");
            return;
        }

        var adjusted = existing with
        {
            Temperature = (float)temperature,
            TopP = (float)topP,
            TopK = topK,
        };

        DA.SetData(0, new GH_ModelConfig(adjusted));
    }
}
