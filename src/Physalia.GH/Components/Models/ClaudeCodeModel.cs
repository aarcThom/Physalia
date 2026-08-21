// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Physalia.Core.Models.Named;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Grasshopper component that configures inference through the locally-installed Claude Code CLI.
/// It uses the user's <c>claude auth login</c> session, so it takes no API key. The model is chosen
/// from a fixed set of CLI aliases exposed to a Picker via <see cref="IPickableValuesSource"/>.
/// </summary>
public class ClaudeCodeModel : PhyBase, IPickableValuesSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeCodeModel"/> class.
    /// </summary>
    public ClaudeCodeModel()
        : base(
            "Claude Code Model",
            "ClaudeCode",
            "Runs inference through the Claude Code CLI already installed on this machine, signed in as you are. No key to store, nothing billed per token.",
            "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("9F2C7A4E-1B6D-4E83-A50F-3C8D2B91E47A");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput("Model", ClaudeCodeConfig.KnownModels) };

    /// <inheritdoc/>
    /// <remarks>
    /// The model list is a fixed set of CLI aliases, so there is nothing to set.
    /// </remarks>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The model list is a fixed set of CLI aliases, so there is nothing to reset.
    /// </remarks>
    public void ResetValues()
    {
    }

    /// <summary>
    /// When dropped onto the canvas, auto-place a Picker wired to the model input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (GhJsonBridge.IsImporting) return;

        if (Params.Input[0].SourceCount > 0) return;

        ComponentHelpers.PickerAdd(this, document, 0);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Model", "M", "Which Claude to use: opus, sonnet or haiku, or a full model id. The Picker placed alongside lists them.", GH_ParamAccess.item, "sonnet");
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "The local Claude Code session, configured as a model. Wire into an LLM Call — there is no Tweaker for this one.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string modelId = "sonnet";
        DA.GetData(0, ref modelId);

        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = "sonnet";
        }

        DA.SetData(0, new GH_ModelConfig(new ClaudeCodeConfig(ModelId: modelId)));
    }
}
