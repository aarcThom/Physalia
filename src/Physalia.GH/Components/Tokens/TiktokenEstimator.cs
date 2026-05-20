// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using CoreTiktoken = Physalia.Core.Tokens.TiktokenEstimator;

namespace Physalia.GH.Components;

/// <summary>
/// Calculates exact token count locally using tiktoken (SharpToken).
/// No API call — calculated in-process. Right-click to select the model encoding.
/// Accurate for OpenAI-family models; reasonable for other BPE-based providers.
/// </summary>
public class TiktokenEstimator : PhyBase
{
    private static readonly string[] s_models =
    {
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-4",
        "gpt-3.5-turbo",
        "text-davinci-003",
    };

    private string _selectedModel = "gpt-4o";
    private CoreTiktoken? _estimator;
    private string _lastBuiltModel = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="TiktokenEstimator"/> class.
    /// </summary>
    public TiktokenEstimator()
        : base(
            "Tiktoken Estimator",
            "TktEst",
            "Calculates exact token count locally using tiktoken. No API call. Right-click to select the model encoding.",
            "Tokens")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("CB4A29DD-C3AD-470D-87FF-CD54EF04F9A8");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D", "Instructions, Conversation, or text string to count.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Tokens", "T", "Exact token count for the selected encoding.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Model Encoding:", null, false, false);
        foreach (var model in s_models)
        {
            Menu_AppendItem(menu, model, OnModelSelected, true, _selectedModel == model);
        }
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("TiktokenModel", _selectedModel);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("TiktokenModel"))
        {
            _selectedModel = reader.GetString("TiktokenModel");
        }

        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        IGH_Goo? goo = null;
        if (!DA.GetData(0, ref goo)) return;

        if (!TokenInputHelper.TryResolve(goo, out var instructions))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input could not be resolved to Instructions, Conversation, or text.");
            return;
        }

        if (_estimator == null || _selectedModel != _lastBuiltModel)
        {
            try
            {
                _estimator = CoreTiktoken.CreateForModel(_selectedModel);
                _lastBuiltModel = _selectedModel;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Unknown model '{_selectedModel}': {ex.Message}");
                return;
            }
        }

        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Encoding: {_selectedModel}");
        DA.SetData(0, _estimator.Estimate(instructions));
    }

    private void OnModelSelected(object sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _selectedModel = item.Text;
            ExpireSolution(true);
        }
    }
}
