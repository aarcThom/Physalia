// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.GH.Helpers;

namespace Physalia.GH.Components;

/// <summary>
/// Abstract base class for all ZOOID component types.
/// Provides the shared variable-parameter machinery and defines the contract
/// that COMPOSER uses to deliver LLM responses and query error state.
/// </summary>
public abstract class ZooidBase : PhyBase, IGH_VariableParameterComponent
{
    // PROPERTIES ====================================================================================

    /// <summary>
    /// Gets the system prompt fragment that instructs the LLM how to format its response
    /// for this Zooid type. Combined with
    /// <see cref="Physalia.Core.Prompts.SystemPrompt.Preamble"/> by COMPOSER before sending.
    /// </summary>
    public abstract string FormatPrompt { get; }

    /// <summary>
    /// Gets the current state of this Zooid as a string — the generated code, component
    /// config, or equivalent. Returns null if no response has been received yet.
    /// </summary>
    public abstract string? State { get; }

    /// <summary>
    /// Gets a human-readable description of this Zooid's current inputs, outputs, and
    /// functionality. Returns null if no response has been received yet.
    /// </summary>
    public abstract string? ZooidDescription { get; }

    /// <summary>
    /// Gets the status message from the most recent LLM response, for display in conversation
    /// history. Set by implementations during <see cref="ApplyLlmResponse"/>.
    /// </summary>
    public string? LastStatusMessage { get; protected set; }

    // CONSTRUCTOR ====================================================================================

    /// <summary>
    /// Initializes a new instance of the <see cref="ZooidBase"/> class.
    /// </summary>
    /// <param name="name">Component name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    /// <param name="subCategory">Grasshopper sub-category.</param>
    protected ZooidBase(string name, string nickname, string description, string subCategory)
        : base(name, nickname, description, subCategory)
    {
    }

    // ABSTRACT METHODS ==============================================================================

    /// <summary>
    /// Parses the raw LLM response, rebuilds component parameters, and schedules a solution
    /// refresh. Implementations must also set <see cref="LastStatusMessage"/>.
    /// </summary>
    /// <param name="rawResponse">The raw JSON string returned by the LLM.</param>
    public abstract void ApplyLlmResponse(string rawResponse);

    /// <summary>
    /// Builds a complete auto-fix prompt describing the current errors, ready to send to
    /// the LLM as a user turn. Returns null if the Zooid currently has no errors to fix.
    /// </summary>
    /// <returns>A formatted fix message string, or null if there are no errors.</returns>
    public abstract string? GetFixMessage();

    /// <summary>
    /// Sanitizes a parameter name to a valid identifier. Replaces any non-alphanumeric,
    /// non-underscore character with an underscore, collapses runs, trims leading/trailing
    /// underscores, and prepends an underscore if the name starts with a digit.
    /// </summary>
    /// <param name="name">The raw name to sanitize.</param>
    /// <returns>A valid sanitized identifier string.</returns>
    protected virtual string SanitizeParamName(string name)
    {
        var clean = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        clean = Regex.Replace(clean, @"_+", "_").Trim('_');

        if (string.IsNullOrEmpty(clean))
            return "param";

        if (char.IsDigit(clean[0]))
            clean = "_" + clean;

        return clean;
    }

    // VIRTUAL METHODS ===============================================================================

    /// <summary>
    /// Returns a prompt fragment describing the user-defined parameters on this Zooid,
    /// for injection into the LLM request. Returns an empty string if there are no user params.
    /// </summary>
    /// <returns>A formatted param description string, or empty if no user params exist.</returns>
    public virtual string UserParamsPrompt()
    {
        var inputs = ParamBuilder.GetUserParams(Params.Input, true);
        var outputs = ParamBuilder.GetUserParams(Params.Output, false);
        return ParamBuilder.UserParamsPrompt(inputs, outputs);
    }

    /// <summary>
    /// Opens an editor for the current Zooid state. No-op by default; override in subclasses.
    /// </summary>
    public virtual void OpenEditor()
    {
    }

    // CONCRETE METHODS ==============================================================================

    /// <summary>
    /// Finds the COMPOSER component in the current document that is linked to this Zooid.
    /// Returns null if none is found.
    /// </summary>
    /// <returns>The linked <see cref="Composer"/>, or null.</returns>
    protected Composer? FindLinkedComposer()
    {
        return OnPingDocument()?.Objects
            .OfType<Composer>()
            .FirstOrDefault(c => c.ZooidComponent?.InstanceGuid == InstanceGuid);
    }

    /// <summary>
    /// Subscribes rename listeners for all current parameters to trigger name sanitization.
    /// Call from <see cref="GH_ActiveObject.AddedToDocument"/> and after parameter changes.
    /// </summary>
    protected void SubscribeUserParamEvents()
    {
        foreach (var p in Params.Input.Concat(Params.Output))
        {
            p.ObjectChanged -= OnUserParamChanged;
            p.ObjectChanged += OnUserParamChanged;
        }
    }

    // VARIABLE PARAMETER INTERFACE ==================================================================

    /// <summary>
    /// Always returns true; users may insert parameters on either side at any index.
    /// </summary>
    /// <param name="side">The parameter side.</param>
    /// <param name="index">The insertion index.</param>
    /// <returns>true.</returns>
    public bool CanInsertParameter(GH_ParameterSide side, int index) => true;

    /// <summary>
    /// Always returns true; users may remove any parameter from either side.
    /// </summary>
    /// <param name="side">The parameter side.</param>
    /// <param name="index">The index of the parameter to remove.</param>
    /// <returns>true.</returns>
    public bool CanRemoveParameter(GH_ParameterSide side, int index) => true;

    /// <summary>
    /// Creates a new generic parameter when the user adds one manually.
    /// Inputs are named "in_N" and outputs "out_N", where N is the lowest available integer.
    /// </summary>
    /// <param name="side">The side on which the parameter will be created.</param>
    /// <param name="index">The index at which the parameter will be inserted.</param>
    /// <returns>A new <see cref="Param_GenericObject"/> with a unique auto-incremented name.</returns>
    public IGH_Param CreateParameter(GH_ParameterSide side, int index)
    {
        if (side == GH_ParameterSide.Input)
        {
            string nick = NextParamName("in", Params.Input.Select(p => p.NickName));
            return new Param_GenericObject { Name = nick, NickName = nick, Description = "user defined parameter", Optional = true };
        }
        else
        {
            string nick = NextParamName("out", Params.Output.Select(p => p.NickName));
            return new Param_GenericObject { Name = nick, NickName = nick, Description = "user defined parameter", Optional = true };
        }
    }

    /// <summary>
    /// Always returns true; no cleanup is required when a parameter is removed.
    /// </summary>
    /// <param name="side">The side from which the parameter is being removed.</param>
    /// <param name="index">The index of the parameter being removed.</param>
    /// <returns>true.</returns>
    public bool DestroyParameter(GH_ParameterSide side, int index) => true;

    /// <summary>
    /// Sanitizes all parameter names after any structural change, and subscribes rename listeners.
    /// </summary>
    public virtual void VariableParameterMaintenance()
    {
        var allParams = Params.Input.Concat(Params.Output).ToList();
        var taken = new HashSet<string>();

        foreach (var p in allParams)
        {
            var clean = SanitizeParamName(p.NickName);
            clean = Deduplicate(clean, taken);
            taken.Add(clean);

            if (clean != p.NickName)
            {
                p.Name = clean;
                p.NickName = clean;
            }
        }

        SubscribeUserParamEvents();
    }

    private void OnUserParamChanged(IGH_DocumentObject sender, GH_ObjectChangedEventArgs e)
    {
        if (e.Type != GH_ObjectEventType.NickName || sender is not IGH_Param p)
        {
            return;
        }

        var clean = SanitizeParamName(p.NickName);

        var taken = new HashSet<string>(
            Params.Input.Concat(Params.Output)
                .Where(other => other != p)
                .Select(other => other.NickName));

        clean = Deduplicate(clean, taken);

        if (clean == p.NickName)
        {
            AfterParamNickNameChanged(p);
            return;
        }

        // Unsubscribe to avoid re-entrancy when setting NickName
        p.ObjectChanged -= OnUserParamChanged;
        p.Name = clean;
        p.NickName = clean;
        p.ObjectChanged += OnUserParamChanged;

        AfterParamNickNameChanged(p);
    }

    /// <summary>
    /// Called after a user param's NickName has been sanitized. Override in subclasses
    /// to react to renames (e.g. sync inner document hooks). No-op by default.
    /// </summary>
    /// <param name="p">The renamed parameter.</param>
    protected virtual void AfterParamNickNameChanged(IGH_Param p)
    {
    }

    private static string Deduplicate(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return name;
        }

        int i = 1;
        string candidate;
        do
        {
            candidate = $"{name}_{i++}";
        }
        while (taken.Contains(candidate));

        return candidate;
    }

    private static string NextParamName(string prefix, IEnumerable<string> existingParams)
    {
        for (int i = 0; ; i++)
        {
            string candidate = $"{prefix}_{i}";
            if (!existingParams.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
