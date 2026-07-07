// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Physalia.Core.Tokens;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Selects a tokenization technique and outputs the corresponding <see cref="ITokenEstimator"/>.
/// An auto-placed Picker is wired to the input when the component is dropped onto the canvas.
/// </summary>
public class TokenizationTechniques : PhyBase, IPickableValuesSource
{
    private static readonly ITokenEstimator _heuristic = new HeuristicTokenEstimator();
    private static readonly Lazy<ITokenEstimator> _tiktoken =
        new Lazy<ITokenEstimator>(() => TiktokenEstimator.CreateForEncoding("cl100k_base"));
    private static readonly ITokenEstimator _anthropic = new AnthropicTokenEstimator();
    private static readonly ITokenEstimator _gemini = new GeminiTokenEstimator();
    private static readonly ITokenEstimator _llamaCpp = new LlamaCppTokenEstimator();
    private static readonly List<string> _techniques =
        new() { "Heuristic", "Tiktoken", "Anthropic", "Gemini", "LlamaCpp" };

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenizationTechniques"/> class.
    /// </summary>
    public TokenizationTechniques()
        : base(
            "Tokenization Techniques",
            "TokTech",
            "Selects a tokenization technique and outputs the corresponding token estimator.",
            "Tokens & Compaction")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B4E7C291-3F5A-4D89-B012-E6A3F7D2C485");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput("Technique", _techniques) };

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values) { }

    /// <inheritdoc/>
    public void ResetValues() { }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Technique", "T", "Tokenization technique. Connect the auto-placed Picker.", GH_ParamAccess.item, "Heuristic");
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ITokenEstimator(), "Tokenization Technique", "T", "The selected token estimator.", GH_ParamAccess.item);
    }

    /// <summary>
    /// When dropped onto the canvas, auto-place a Picker wired to the technique input.
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
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string technique = string.Empty;
        if (!DA.GetData(0, ref technique)) return;

        ITokenEstimator estimator = technique switch
        {
            "Tiktoken" => _tiktoken.Value,
            "Anthropic" => _anthropic,
            "Gemini" => _gemini,
            "LlamaCpp" => _llamaCpp,
            _ => _heuristic,
        };

        DA.SetData(0, new GH_ITokenEstimator(estimator));
    }
}
