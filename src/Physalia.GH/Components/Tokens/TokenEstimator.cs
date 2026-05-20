// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Physalia.Core.Common;
using Physalia.Core.Models.Protocol;
using Physalia.Core.Tokens;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Estimates token count by dispatching to the appropriate counting strategy
/// for the selected <see cref="ITokenEstimator"/>.
/// </summary>
public class TokenEstimator : PhyBase
{
    private readonly HttpClient _httpClient = new HttpClient();

    private string _lastTechnique = string.Empty;
    private string _lastTiktokenModel = string.Empty;
    private TiktokenEstimator? _tiktokenCache;

    private string _lastAsyncKey = string.Empty;
    private int? _lastAsyncResult;
    private string? _asyncWarning;
    private CancellationTokenSource? _cts;

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
    /// When dropped onto the canvas, auto-place a value list wired to the model input.
    /// </summary>
    /// <param name="document">The active Grasshopper document.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        if (Params.Input[2].SourceCount > 0) return;

        var list = ComponentHelpers.ValueListAdd(this, document, 2, string.Empty);
        list.ListItems.Clear();
        list.ListItems.Add(new GH_ValueListItem("N/A", "\"N/A\""));
        list.SelectItem(0);
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
            PopulateValueList(estimator);
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Select a model from the value list.");
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

            default:
                DA.SetData(0, estimator.Estimate(instructions));
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

            if (result is Result<int, LlmError>.Ok ok)
            {
                _lastAsyncResult = ok.Value;
            }
            else if (result is Result<int, LlmError>.Err err)
            {
                _asyncWarning = $"{_lastTechnique} token count failed: {err.Error.Message}";
            }

            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
        }, ct);
    }

    private void PopulateValueList(ITokenEstimator estimator)
    {
        var valueList = Params.Input[2].Sources.OfType<GH_ValueList>().FirstOrDefault();
        if (valueList is null) return;

        OnPingDocument()?.ScheduleSolution(1, _ =>
        {
            valueList.ListItems.Clear();

            if (estimator is TiktokenEstimator)
            {
                valueList.ListItems.Add(new GH_ValueListItem("gpt-4", "\"gpt-4\""));
                valueList.ListItems.Add(new GH_ValueListItem("gpt-3.5-turbo", "\"gpt-3.5-turbo\""));
                valueList.ListItems.Add(new GH_ValueListItem("text-davinci-003", "\"text-davinci-003\""));
                valueList.ListItems.Add(new GH_ValueListItem("text-davinci-002", "\"text-davinci-002\""));
            }
            else
            {
                valueList.ListItems.Add(new GH_ValueListItem("N/A", "\"N/A\""));
            }

            if (valueList.ListItems.Count > 0)
                valueList.SelectItem(0);

            valueList.ExpireSolution(true);
        });
    }
}
