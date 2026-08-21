// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Models.Named;
using Physalia.Core.Providers.Codex;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Grasshopper component that configures inference through the locally-installed OpenAI Codex CLI.
/// It uses the user's <c>codex login</c> session, so it takes no API key. Both inputs are exposed
/// to a Picker via <see cref="IPickableValuesSource"/>: the model list is fetched from the CLI
/// itself (which models an account may use is plan-dependent), the reasoning efforts are the
/// standard set.
/// </summary>
public class CodexModel : PhyBase, IPickableValuesSource
{
    private const string ModelInputName = "Model";
    private const string EffortInputName = "Effort";

    private List<string> _availableModels = new(CodexConfig.KnownModels);
    private CancellationTokenSource? _cts;
    private bool _fetchStarted;
    private string? _fetchWarning;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodexModel"/> class.
    /// </summary>
    public CodexModel()
        : base(
            "Codex Model",
            "Codex",
            "Runs inference through the OpenAI Codex CLI already installed on this machine, signed in as you are. No key to store, nothing billed per token.",
            "Models")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3E7B14C6-2A0D-4F58-9C31-6D8A5B2F70E9");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[]
        {
            new PickableInput(ModelInputName, _availableModels),
            new PickableInput(EffortInputName, CodexConfig.KnownReasoningEfforts),
        };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        if (inputName == ModelInputName)
        {
            _availableModels = new List<string>(values);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Resets to the seed list rather than to nothing, so a Picker is never left with an empty menu
    /// while the CLI is being asked.
    /// </remarks>
    public void ResetValues() => _availableModels = new List<string>(CodexConfig.KnownModels);

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

    /// <summary>
    /// Cancels any in-flight model-list fetch so it cannot outlive the component.
    /// </summary>
    /// <param name="document">The document this component was removed from.</param>
    public override void RemovedFromDocument(GH_Document document)
    {
        _cts?.Cancel();
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Model", "M", "Which model to use. The Picker placed alongside is filled by asking the CLI itself. Leave it empty to take whatever the CLI would choose.", GH_ParamAccess.item, string.Empty);
        pManager.AddTextParameter("Effort", "E", "How hard to think: low, medium, high or xhigh. The Picker placed alongside lists them. Leave it empty for the model's own default.", GH_ParamAccess.item, string.Empty);
        pManager[1].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "The local Codex session, configured as a model. Wire into an LLM Call — there is no Tweaker for this one.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_fetchWarning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, _fetchWarning);
            _fetchWarning = null;
        }

        string modelId = string.Empty;
        DA.GetData(0, ref modelId);

        string effort = string.Empty;
        DA.GetData(1, ref effort);

        if (!CodexProvider.IsCliAvailable())
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Codex CLI not found on PATH. Install it (npm install -g @openai/codex) and run `codex login`.");
            return;
        }

        StartModelFetch();

        var config = new CodexConfig(ModelId: (modelId ?? string.Empty).Trim())
        {
            ReasoningEffort = string.IsNullOrWhiteSpace(effort) ? null : effort.Trim(),
        };

        DA.SetData(0, new GH_ModelConfig(config));
    }

    // Asks the CLI once per component for the models this account may use, then refreshes the
    // Picker. Fire-and-forget on a background thread: the query starts a short-lived CLI process,
    // and the seed list keeps the Picker usable until the answer lands.
    private void StartModelFetch()
    {
        if (_fetchStarted)
        {
            return;
        }

        _fetchStarted = true;
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        Task.Run(async () =>
        {
            var provider = new CodexProvider();
            Result<IReadOnlyList<string>, LlmError> result =
                await provider.GetAvailableModelsAsync(new CodexConfig(), ct);

            if (ct.IsCancellationRequested) return;

            if (result.IsOk(out IReadOnlyList<string>? models, out LlmError? err) && models is { Count: > 0 })
            {
                SetValues(ModelInputName, models);
            }
            else if (err != null)
            {
                _fetchWarning = $"Could not read the Codex model list: {err.Message}. Showing known models.";
            }

            // Refresh the Picker wired to the model input so its menu shows the live list.
            OnPingDocument()?.ScheduleSolution(1, _ =>
            {
                foreach (var source in Params.Input[0].Sources)
                {
                    (source.Attributes?.GetTopLevel?.DocObject as IGH_ActiveObject)?.ExpireSolution(false);
                }

                ExpireSolution(false);
            });
        }, ct);
    }
}
