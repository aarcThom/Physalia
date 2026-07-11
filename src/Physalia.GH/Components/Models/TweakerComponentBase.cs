// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel;
using Physalia.Core.Models;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Base for Tweaker components that adjust inference parameters on a model
/// configuration: Model + Temperature + Top P + one provider-specific integer
/// parameter (e.g. Top K or Max Tokens). Owns the read/type-check/mutate/emit
/// flow; subclasses declare their parameter wording, defaults, and the record
/// mutation.
/// </summary>
/// <typeparam name="TConfig">The concrete config record this tweaker adjusts.</typeparam>
public abstract class TweakerComponentBase<TConfig> : PhyBase
    where TConfig : ModelConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TweakerComponentBase{TConfig}"/> class.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected TweakerComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, "Models")
    {
    }

    /// <summary>
    /// Gets the description for the Model input parameter.
    /// </summary>
    protected abstract string ModelInputDescription { get; }

    /// <summary>
    /// Gets the description for the adjusted Model output parameter.
    /// </summary>
    protected abstract string ModelOutputDescription { get; }

    /// <summary>
    /// Gets the description for the Temperature input, including the provider's range.
    /// </summary>
    protected abstract string TemperatureDescription { get; }

    /// <summary>
    /// Gets the default Top P value (e.g. 0.95 for Gemini, 1.0 elsewhere).
    /// </summary>
    protected abstract double TopPDefault { get; }

    /// <summary>
    /// Gets the default for the provider-specific integer parameter.
    /// </summary>
    protected abstract int ThirdParamDefault { get; }

    /// <summary>
    /// Gets the error message shown when the input config is not <typeparamref name="TConfig"/>.
    /// </summary>
    protected abstract string WrongConfigTypeMessage { get; }

    /// <summary>
    /// Registers the provider-specific integer parameter at input index 3
    /// (e.g. Top K or Max Tokens).
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected abstract void RegisterThirdParam(GH_InputParamManager pManager);

    /// <summary>
    /// Applies the adjusted values to the config record.
    /// </summary>
    /// <param name="existing">The incoming config.</param>
    /// <param name="temperature">The sampling temperature.</param>
    /// <param name="topP">The nucleus sampling threshold.</param>
    /// <param name="thirdValue">The provider-specific integer value.</param>
    /// <returns>The adjusted config record.</returns>
    protected abstract TConfig Adjust(TConfig existing, float temperature, float topP, int thirdValue);

    /// <inheritdoc/>
    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", ModelInputDescription, GH_ParamAccess.item);
        pManager.AddNumberParameter("Temperature", "T", TemperatureDescription, GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Top P", "P", "Nucleus sampling threshold (0.0–1.0).", GH_ParamAccess.item, TopPDefault);
        RegisterThirdParam(pManager);
        RegisterAdditionalParams(pManager);
    }

    /// <summary>
    /// Registers subclass-specific inputs after the shared four (i.e. starting at
    /// index 4). Appending here keeps saved-document param layouts stable. Default
    /// registers nothing.
    /// </summary>
    /// <param name="pManager">The input parameter manager.</param>
    protected virtual void RegisterAdditionalParams(GH_InputParamManager pManager)
    {
    }

    /// <summary>
    /// Applies subclass-specific inputs (registered via <see cref="RegisterAdditionalParams"/>)
    /// to the already-adjusted config. Default returns the config unchanged.
    /// </summary>
    /// <param name="config">The config returned by <see cref="Adjust"/>.</param>
    /// <param name="da">The data access for the current solve.</param>
    /// <returns>The adjusted config.</returns>
    protected virtual TConfig AdjustAdditional(TConfig config, IGH_DataAccess da) => config;

    /// <inheritdoc/>
    protected sealed override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ModelConfig(), "Model", "M", ModelOutputDescription, GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        var goo = new GH_ModelConfig();
        double temperature = 1.0;
        double topP = TopPDefault;
        int thirdValue = ThirdParamDefault;

        if (!DA.GetData(0, ref goo)) return;
        DA.GetData(1, ref temperature);
        DA.GetData(2, ref topP);
        DA.GetData(3, ref thirdValue);

        if (goo.Value is not TConfig existing)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, WrongConfigTypeMessage);
            return;
        }

        DA.SetData(0, new GH_ModelConfig(AdjustAdditional(Adjust(existing, (float)temperature, (float)topP, thirdValue), DA)));
    }
}
