// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Models.Named;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Adjusts the inference parameters of a llama.cpp model configuration.
/// </summary>
public class LlamaCppTweaker : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaCppTweaker"/> class.
    /// </summary>
    public LlamaCppTweaker()
        : base("LlamaCpp Tweaker", "LlamaTwk", "Adjusts temperature, top-p, and max tokens on a llama.cpp model configuration.", "Config")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4B5C6D7E-8F9A-0B1C-D2E3-F4A5B6C7D8E9");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "model", "M", "LlamaCpp model configuration to adjust.", GH_ParamAccess.item);
        pManager.AddNumberParameter("temperature", "t", "Sampling temperature (0.0–2.0). llama.cpp default is 0.8.", GH_ParamAccess.item, 0.8);
        pManager.AddNumberParameter("top p", "p", "Nucleus sampling threshold (0.0–1.0).", GH_ParamAccess.item, 1.0);
        pManager.AddIntegerParameter("max tokens", "T", "Maximum number of tokens to generate.", GH_ParamAccess.item, 2048);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "model", "M", "Adjusted llama.cpp model configuration.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var goo = new GH_ModelConfig();
        double temperature = 0.8;
        double topP = 1.0;
        int maxTokens = 2048;

        if (!DA.GetData(0, ref goo)) return;
        DA.GetData(1, ref temperature);
        DA.GetData(2, ref topP);
        DA.GetData(3, ref maxTokens);

        if (goo.Value is not LlamaCppConfig existing)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input model must be a LlamaCpp configuration.");
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
