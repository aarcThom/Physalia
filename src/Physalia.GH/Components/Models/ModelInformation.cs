// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Models;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Looks up a model configuration in the LiteLLM model database and outputs its
/// context limits and capability flags. The database is fetched once at startup.
/// </summary>
public class ModelInformation : PhyBase
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static IReadOnlyDictionary<string, ModelEntry>? _models;
    private static bool _isFetching;

    private string? _warning;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelInformation"/> class.
    /// </summary>
    public ModelInformation()
        : base(
            "Model Information",
            "ModelInfo",
            "Looks up a model in the LiteLLM database and returns its context limits and capability flags.",
            "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A005D956-B74F-4B1D-907E-637D0AD53A96");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "The model configuration to look up.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Max Input", "I", "Maximum input tokens (context window size).", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Max Output", "O", "Maximum output tokens the model can generate.", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Image Capable", "V", "Whether the model accepts image inputs.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_warning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _warning);
            _warning = null;
        }

        if (_models == null && !_isFetching)
        {
            _isFetching = true;
            StartFetch();
        }

        var goo = new GH_ModelConfig();
        if (!DA.GetData(0, ref goo)) return;

        if (goo.Value is not ModelConfig config)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid model configuration.");
            return;
        }

        if (_models == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Loading model database...");
            return;
        }

        var entry = ModelList.Find(_models, config.ModelId);

        if (entry == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"'{config.ModelId}' not found in model database.");
            DA.SetData(0, 0);
            DA.SetData(1, 0);
            DA.SetData(2, false);
            return;
        }

        DA.SetData(0, entry.MaxInputTokens);
        DA.SetData(1, entry.MaxOutputTokens);
        DA.SetData(2, entry.SupportsVision);
    }

    private void StartFetch()
    {
        Task.Run(async () =>
        {
            var result = await ModelList.FetchAsync(_httpClient);

            if (result is Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Ok ok)
            {
                _models = ok.Value;
            }
            else if (result is Result<IReadOnlyDictionary<string, ModelEntry>, LlmError>.Err err)
            {
                _isFetching = false;
                _warning = $"Failed to load model database: {err.Error.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        });
    }
}
