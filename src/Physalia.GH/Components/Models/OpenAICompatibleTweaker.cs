// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Models.Protocol;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Adjusts the inference parameters of any OpenAI-compatible model configuration.
/// </summary>
public class OpenAICompatibleTweaker : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAICompatibleTweaker"/> class.
    /// </summary>
    public OpenAICompatibleTweaker()
        : base(
            "OpenAI Compatible Tweaker",
            "OAITwk",
            "Adjusts temperature, top-p, and max tokens on any OpenAI-compatible model configuration.",
            "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D5E6F7A8-B9C0-4D1E-F2A3-B4C5D6E7F8A9");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "OpenAI-compatible model configuration to adjust.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Temperature", "T", "Sampling temperature (0.0–2.0).", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Top P", "P", "Nucleus sampling threshold (0.0–1.0).", GH_ParamAccess.item, 1.0);
        pManager.AddIntegerParameter("Max Tokens", "N", "Maximum number of tokens to generate.", GH_ParamAccess.item, 4096);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Adjusted OpenAI-compatible model configuration.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var goo = new GH_ModelConfig();
        double temperature = 1.0;
        double topP = 1.0;
        int maxTokens = 4096;

        if (!DA.GetData(0, ref goo)) return;
        DA.GetData(1, ref temperature);
        DA.GetData(2, ref topP);
        DA.GetData(3, ref maxTokens);

        if (goo.Value is not OpenAIProtocolConfig existing)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input model must be an OpenAI-compatible configuration.");
            return;
        }

        var adjusted = existing with
        {
            Temperature = (float)temperature,
            TopP = (float)topP,
            MaxTokens = maxTokens,
        };

        DA.SetData(0, new GH_ModelConfig(adjusted));
    }
}
