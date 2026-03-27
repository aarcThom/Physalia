// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Parsing;
using Physalia.Core.Prompts;
using Physalia.Core.Providers;
using Physalia.GH.Attributes;
using Physalia.GH.Helpers;
using Physalia.GH.ParamTypes;

namespace Physalia.GH.Components;

/// <summary>
/// The CREST component receives an LLM provider from MODEL SELECTOR, accepts a user prompt,
/// calls the LLM API asynchronously, and forwards the parsed response to a linked ZOOID component.
/// </summary>
public class Crest : PhyBase
{
    private int _maxAutoFixAttemps;
    private Task? _pendingRequest;
    private string? _errorMsg;
    private string? _lastPrompt;
    private bool _autoFix;
    private int _autoFixAttempts;
    private bool _waitingForAutoFix;

    /// <summary>
    /// Gets the unique ID for this component. Do not change this ID after release.
    /// </summary>
    public override Guid ComponentGuid
    {
        get { return new Guid("B904F3D0-72CC-4B43-A15E-497CE8478638"); }
    }

    /// <summary>
    /// Gets or sets the ZOOID component that receives the LLM-generated script.
    /// </summary>
    public Zooid? ZooidComponent { get; set; }

    /// <summary>
    /// Gets or sets the instance GUID of the linked ZOOID component, used for serialization and reconnection.
    /// </summary>
    public Guid ZooidGuid { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Crest"/> class.
    /// </summary>
    public Crest()
        : base("CREST", "CREST", "Description", "Core")
    {
    }

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    /// <param name="pManager">The GH_InputParamManager for registering input parameters.</param>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
        pManager.AddParameter(new LlmProviderGhParam(), "Llm", "Llm", "Large language model from DREAM", GH_ParamAccess.item);
        pManager.AddTextParameter("Prompt", "Prmpt", "The prompt", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Send", "Snd", "Send Prompt to defined model", GH_ParamAccess.item);
        pManager.AddBooleanParameter("AutoFix", "Fix", "Set True if you want the LLM to attempt to fix errors as they occur. Defaults to True.", GH_ParamAccess.item, true);
        pManager.AddIntegerParameter("Fix Attemps", "Fix #", "Number of times you want to send error codes back to the LLM for fixing. Defaults to 3", GH_ParamAccess.item, 3);
    }

    /// <summary>
    /// Registers all the output parameters for this component.
    /// </summary>
    /// <param name="pManager">The GH_OutputParamManager for registering output parameters.</param>
    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // INPUTS -------------------------------
        LlmProviderGoo? llmGoo = null;
        string? prompt = null;
        bool send = false;

        if (!DA.GetData(0, ref llmGoo))
        {
            return;
        }

        if (!DA.GetData(1, ref prompt))
        {
            return;
        }

        if (!DA.GetData(2, ref send))
        {
            return;
        }

        if (!DA.GetData(3, ref _autoFix))
        {
            return;
        }

        if (!DA.GetData(4, ref _maxAutoFixAttemps))
        {
            return;
        }

        // error message from original LLM call or auto fix attemps
        if (_errorMsg != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _errorMsg);
            _errorMsg = null;
        }

        if (ZooidComponent == null)
        {
            return; // no zooid linked yet
        }

        if (send && (_pendingRequest == null || _pendingRequest.IsCompleted) && !_waitingForAutoFix)
        {
            _autoFixAttempts = 0;

            var llm = llmGoo.Value;
            _pendingRequest = SendRequestAsync(prompt, llm, ZooidComponent);
        }
    }

    // OVERRIDE METHODS ======================================================================================================

    /// <summary>
    /// Assigns the custom <see cref="CrestAttrib"/> attribute class to this component.
    /// </summary>
    public override void CreateAttributes() => m_attributes = new CrestAttrib(this);

    /// <summary>
    /// Serializes the linked ZOOID's instance GUID so the connection survives save/load.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("ZooidGuid", ZooidGuid.ToString());
        return base.Write(writer);
    }

    /// <summary>
    /// Deserialises the linked ZOOID's instance GUID. The live reference is restored later in
    /// <see cref="AddedToDocument"/>, once all document objects are available.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IReader reader)
    {
        string guidStr = string.Empty;
        if (reader.TryGetString("ZooidGuid", ref guidStr) && Guid.TryParse(guidStr, out Guid guid))
        {
            ZooidGuid = guid;
        }

        return base.Read(reader);
    }

    /// <summary>
    /// Schedules a reconnect to the linked ZOOID after the document finishes loading,
    /// ensuring all objects are present before the GUID lookup runs.
    /// </summary>
    /// <param name="document">The document this component was added to.</param>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        if (ZooidGuid != Guid.Empty)
        {
            document.ScheduleSolution(10, ReconnectZooid);
        }
    }

    // LLM REQUESTS METHODS ======================================================================================================

    // THE MAIN LLM REQUEST
    private async Task SendRequestAsync(
        string prompt,
        LlmProvider llModel,
        Zooid zooid)
    {
        Message = "Calling API";

        // GET PRE-EXISTING INPUTS AND OUTPUTS
        var zooidInputs = ParamBuilder.GetUserParams(zooid.Params.Input, true);
        var zooidOutputs = ParamBuilder.GetUserParams(zooid.Params.Output, false);
        var paramPrompt = ParamBuilder.UserParamsPrompt(zooidInputs, zooidOutputs);

        try
        {
            // SEND THE PROMPT - GET AND FORMAT THE RESULT
            var rawResult = await llModel.SendPromptAsync(SystemPrompt.Default, prompt + paramPrompt);
            var formattedResult = ResponseParser.Parse(rawResult);
            _lastPrompt = prompt;

            // SEND TO ZOOID AND EXPIRE SOLUTION
            zooid.ReceiveResponse(formattedResult);
            Message = "Done";
            ExpireSolution(true);

            // WAIT A BIT AND THEN AUTOFIX IF NEED BE
            if (_autoFix && _autoFixAttempts < _maxAutoFixAttemps)
            {
                _waitingForAutoFix = true;

                var currentDoc = OnPingDocument();
                currentDoc.ScheduleSolution(1500, _ => TriggerAutoFix(prompt, llModel, zooid, formattedResult));
            }
        }
        catch (Exception ex)
        {
            _errorMsg = ex.InnerException?.Message ?? ex.Message;
            Message = "Error";
            ExpireSolution(true);
        }
    }

    // CHECKS IF FIX IS NEEDED / ALLOWED AND THEN RECALLS THE LLM
    private void TriggerAutoFix(string originalPrompt, LlmProvider llModel, Zooid zooid, ScriptResponse lastResponse)
    {
        _waitingForAutoFix = false;

        if (_autoFix && _autoFixAttempts < _maxAutoFixAttemps && ShouldAutoFix(zooid))
        {
            _autoFixAttempts++;
            _pendingRequest = AutoFixAsync(originalPrompt, llModel, zooid, lastResponse);
        }
    }

    // DETERMINES WHETHER OR NOT AN AUTOFIX SHOULD BE ATTEMPTED
    private static bool ShouldAutoFix(Zooid zooid)
    {
        bool hasErrors = !string.IsNullOrEmpty(zooid.LastRuntimeError);

        // can't really fix components that aren't fully connected
        bool allInputsUnconnected = zooid.Params.Input.Count > 0 && zooid.Params.Input.All(p => p.Sources.Count == 0);
        return hasErrors && !allInputsUnconnected;
    }

    // THE AUTOFIX REQUEST
    private async Task AutoFixAsync(string originalPrompt, LlmProvider llModel, Zooid zooid, ScriptResponse lastResponse)
    {
        Message = $"Fixing ({_autoFixAttempts}/{_maxAutoFixAttemps})";

        var fixPrompt = BuildFixPrompt(originalPrompt, lastResponse, zooid.LastDiagnostics, zooid.LastRuntimeError);

        try
        {
            var rawResult = await llModel.SendPromptAsync(SystemPrompt.Default, fixPrompt);
            var formattedResult = ResponseParser.Parse(rawResult);
            zooid.ReceiveResponse(formattedResult);
            Message = "Done";
            ExpireSolution(true);

            if (_autoFix && _autoFixAttempts < _maxAutoFixAttemps)
            {
                _waitingForAutoFix = true;
                OnPingDocument()?.ScheduleSolution(1500, _ => TriggerAutoFix(originalPrompt, llModel, zooid, formattedResult));
            }
        }
        catch (Exception ex)
        {
            _errorMsg = ex.InnerException?.Message ?? ex.Message;
            Message = "Fix failed";
            ExpireSolution(true);
        }
    }

    private static string BuildFixPrompt(string originalPrompt, ScriptResponse response, IReadOnlyList<Diagnostic> diagnostics, string? runtimeError)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"The following script was generated for this task: \"{originalPrompt}\"");
        sb.AppendLine();
        sb.AppendLine("The script has errors that need fixing. Return a corrected JSON response in the same format.");
        sb.AppendLine();
        sb.AppendLine("Current script:");
        sb.AppendLine(response.Script);

        if (diagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Static analysis errors:");
            foreach (var d in diagnostics)
            {
                sb.AppendLine($"  {d}");
            }
        }

        if (!string.IsNullOrEmpty(runtimeError))
        {
            sb.AppendLine();
            sb.AppendLine("Runtime error:");
            sb.AppendLine($"  {runtimeError}");
        }

        return sb.ToString();
    }

    private void ReconnectZooid(GH_Document doc)
    {
        var obj = doc.FindObject(ZooidGuid, true);
        if (obj is Zooid zooid)
        {
            ZooidComponent = zooid;
        }
    }
}
