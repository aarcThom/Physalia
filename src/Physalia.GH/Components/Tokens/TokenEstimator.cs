// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.Common;
using Physalia.Core.Models.Protocol;
using Physalia.Core.Tokens;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Estimates token count by dispatching to the appropriate counting strategy
/// for the selected <see cref="ITokenEstimator"/>.
/// </summary>
public class TokenEstimator : PhyBase, IPickableValuesSource
{
    // Shared, not per-instance: HttpClient is thread-safe and reuse avoids socket exhaustion.
    private static readonly HttpClient _httpClient = new();

    private string _lastTechnique = string.Empty;
    private string _lastTiktokenModel = string.Empty;
    private TiktokenEstimator? _tiktokenCache;

    private string _lastAsyncKey = string.Empty;
    private int? _lastAsyncResult;
    private string? _asyncWarning;
    private CancellationTokenSource? _cts;

    private List<string> _tiktokenNames = new() { "N/A" };

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenEstimator"/> class.
    /// </summary>
    public TokenEstimator()
        : base(
            "Token Estimator",
            "TokEst",
            "Estimates token count using the selected tokenization technique.",
            "Tokens")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A2F7B3E1-5C84-4D96-8012-B9D4E7F3A215");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput("Tiktoken Name", _tiktokenNames) };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        if (inputName == "Tiktoken Name")
            _tiktokenNames = new List<string>(values);
    }

    /// <inheritdoc/>
    public void ResetValues() => _tiktokenNames.Clear();

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ITokenEstimator(), "Tokenization Technique", "T", "The tokenization technique to use for estimation.", GH_ParamAccess.item);
        pManager.AddGenericParameter("Data", "D", "Instructions, Conversation, or text string to estimate.", GH_ParamAccess.item);
        pManager.AddTextParameter("Tiktoken Name", "TN", "Tiktoken model name. Auto-populated based on technique; shows N/A for non-tiktoken estimators.", GH_ParamAccess.item, "N/A");
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", "Model configuration for API-backed estimators (Anthropic, Gemini, LlamaCpp).", GH_ParamAccess.item);
        pManager[3].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Token Count", "N", "Estimated token count.", GH_ParamAccess.item);
    }

    /// <summary>
    /// When dropped onto the canvas, auto-place a Picker wired to the Tiktoken Name input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (GhJsonBridge.IsImporting) return;

        if (Params.Input[2].SourceCount > 0) return;

        ComponentHelpers.PickerAdd(this, document, 2);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_asyncWarning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _asyncWarning);
            _asyncWarning = null;
        }

        var techniqueGoo = new GH_ITokenEstimator();
        if (!DA.GetData(0, ref techniqueGoo) || techniqueGoo.Value is null) return;

        var estimator = techniqueGoo.Value;
        string techniqueName = estimator.GetType().Name;

        if (techniqueName != _lastTechnique)
        {
            _lastTechnique = techniqueName;
            _tiktokenCache = null;
            _lastAsyncKey = string.Empty;
            _lastAsyncResult = null;
            UpdateTiktokenNames(estimator);
        }

        IGH_Goo? dataGoo = null;
        if (!DA.GetData(1, ref dataGoo)) return;

        string model = "N/A";
        DA.GetData(2, ref model);

        var configGoo = new GH_ModelConfig();
        bool hasConfig = DA.GetData(3, ref configGoo);

        if (!TokenInputHelper.TryResolve(dataGoo, out var instructions))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input could not be resolved to Instructions, Conversation, or text.");
            return;
        }

        switch (estimator)
        {
            case HeuristicTokenEstimator heuristic:
                DA.SetData(0, heuristic.Estimate(instructions));
                break;

            case TiktokenEstimator _:
                if (string.IsNullOrEmpty(model) || model == "N/A")
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Select a model from the Picker.");
                    return;
                }
                if (model != _lastTiktokenModel || _tiktokenCache is null)
                {
                    _lastTiktokenModel = model;
                    try
                    {
                        _tiktokenCache = TiktokenEstimator.CreateForModel(model);
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Tiktoken model '{model}' is not supported: {ex.Message}");
                        return;
                    }
                }
                DA.SetData(0, _tiktokenCache.Estimate(instructions));
                break;

            case AnthropicTokenEstimator _:
                if (!hasConfig || configGoo.Value is not AnthropicProtocolConfig anthropicConfig)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Anthropic estimation requires an Anthropic model configuration.");
                    return;
                }
                RunAsync(DA,
                    techniqueName + "||" + TokenInputHelper.BuildDataKey(instructions) + "||" + anthropicConfig.ModelId + anthropicConfig.BaseUrl + anthropicConfig.ApiKey,
                    ct => AsyncTokenEstimation.CountAnthropicAsync(instructions, anthropicConfig, _httpClient, ct),
                    "Counting tokens via Anthropic API...");
                break;

            case GeminiTokenEstimator _:
                if (!hasConfig || configGoo.Value is not GeminiProtocolConfig geminiConfig)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Gemini estimation requires a Gemini model configuration.");
                    return;
                }
                RunAsync(DA,
                    techniqueName + "||" + TokenInputHelper.BuildDataKey(instructions) + "||" + geminiConfig.ModelId + geminiConfig.BaseUrl + geminiConfig.ApiKey,
                    ct => AsyncTokenEstimation.CountGeminiAsync(instructions, geminiConfig, _httpClient, ct),
                    "Counting tokens via Gemini API...");
                break;

            case LlamaCppTokenEstimator _:
                if (!hasConfig || configGoo.Value is not OpenAIProtocolConfig llamaConfig)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "LlamaCpp estimation requires an OpenAI-compatible model configuration.");
                    return;
                }
                RunAsync(DA,
                    techniqueName + "||" + TokenInputHelper.BuildDataKey(instructions) + "||" + llamaConfig.BaseUrl + llamaConfig.ApiKey,
                    ct => AsyncTokenEstimation.CountLlamaCppAsync(instructions, llamaConfig, _httpClient, ct),
                    "Counting tokens via llama-server...");
                break;

            case ISyncTokenEstimator sync:
                DA.SetData(0, sync.Estimate(instructions));
                break;

            default:
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "This estimator cannot be counted synchronously and has no API-backed counter wired here.");
                break;
        }
    }

    private void RunAsync(
        IGH_DataAccess DA,
        string key,
        Func<CancellationToken, Task<Result<int, LlmError>>> countFunc,
        string pendingRemark)
    {
        if (key != _lastAsyncKey)
        {
            _lastAsyncKey = key;
            _lastAsyncResult = null;
            StartAsyncCount(countFunc);
        }

        if (_lastAsyncResult.HasValue)
        {
            DA.SetData(0, _lastAsyncResult.Value);
        }
        else
        {
            DA.SetData(0, 0);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, pendingRemark);
        }
    }

    private void StartAsyncCount(Func<CancellationToken, Task<Result<int, LlmError>>> countFunc)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            var result = await countFunc(ct);

            if (ct.IsCancellationRequested) return;

            if (result.IsOk(out var count, out var err))
            {
                _lastAsyncResult = count;
            }
            else
            {
                _asyncWarning = $"{_lastTechnique} token count failed: {err.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }, ct);
    }

    private void UpdateTiktokenNames(ITokenEstimator estimator)
    {
        if (estimator is TiktokenEstimator)
        {
            SetValues("Tiktoken Name", new[] { "gpt-4", "gpt-3.5-turbo", "text-davinci-003", "text-davinci-002" });
        }
        else
        {
            SetValues("Tiktoken Name", new[] { "N/A" });
        }

        OnPingDocument()?.ScheduleSolution(1, _ =>
        {
            foreach (var source in Params.Input[2].Sources)
                (source.Attributes?.GetTopLevel?.DocObject as IGH_ActiveObject)?.ExpireSolution(false);
            ExpireSolution(true);
        });
    }
}
