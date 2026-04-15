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
/// The COMPOSER component receives an LLM provider from MODEL SELECTOR, accepts a user prompt,
/// calls the LLM API asynchronously, and forwards the parsed response to a linked ZOOID component.
/// </summary>
public class Composer : PhyBase
{
    // FIELDS ==========================================================================================
    private int _maxAutoFixAttemps;
    private Task? _pendingRequest;
    private string? _errorMsg;
    private bool _autoFix;
    private bool _waitingForAutoFix;
    private bool _isBusy = false; // used to prevent connected prompt component from passing messages mid call.
    private bool _llmConnected = false; // used to prevent prompt from entering prompt if no llm hooked up.
    private string _debugLog = string.Empty;

    // PROPERTIES =======================================================================================

    /// <summary>
    /// Gets the unique ID for this component. Do not change this ID after release.
    /// </summary>
    public override Guid ComponentGuid
    {
        get { return new Guid("B904F3D0-72CC-4B43-A15E-497CE8478638"); }
    }

    /// <summary>
    /// Gets or sets the ZOOID component that receives the LLM-generated response.
    /// </summary>
    public ZooidBase? ZooidComponent { get; set; }

    /// <summary>
    /// Gets or sets the instance GUID of the linked ZOOID component, used for serialization and reconnection.
    /// </summary>
    public Guid ZooidGuid { get; set; }

    /// <summary>
    /// Gets a value that will be set to true if an API call is currently being made or an autofix is queued.
    /// </summary>
    public bool IsBusy => _isBusy;

    /// <summary>
    /// Gets true if LLM is actively connected.
    /// </summary>
    public bool LlmConnected => _llmConnected;

    // CONSTRUCTOR =======================================================================================

    /// <summary>
    /// Initializes a new instance of the <see cref="Composer"/> class.
    /// </summary>
    public Composer()
        : base("Composer", "Composer", "Description", "Core")
    {
        IconPath = "Physalia.GH.Resources.composer.png";
    }

    // GH COMPONENT OVERRIDES ============================================================================================

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    /// <param name="pManager">The GH_InputParamManager for registering input parameters.</param>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
        pManager.AddParameter(new LlmProviderGhParam(), "Llm", "Llm", "Large language model.", GH_ParamAccess.item);
        pManager.AddParameter(new ConversationGhParam(), "Prompt", "Prt", "The conversation from PROMPT", GH_ParamAccess.item);
        pManager.AddBooleanParameter("AutoFix", "Fix", "Set True if you want the LLM to attempt to fix errors as they occur. Defaults to True.", GH_ParamAccess.item, true);
        pManager.AddIntegerParameter("Fix Attempts", "Fix #", "Number of times you want to send error codes back to the LLM for fixing. Defaults to 3", GH_ParamAccess.item, 3);
    }

    /// <summary>
    /// Registers all the output parameters for this component.
    /// </summary>
    /// <param name="pManager">The GH_OutputParamManager for registering output parameters.</param>
    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Log", "Log", "Debug log of LLM responses and error messages, newest first.", GH_ParamAccess.item);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // INPUTS -------------------------------
        LlmProviderGoo? llmGoo = null;
        ConversationGoo? convGoo = null;

        if (!DA.GetData(0, ref llmGoo) || llmGoo == null)
        {
            _llmConnected = false;
            return;
        }

        // needed to prevent the connected prompt component from being able to enter text when a model isn't specified
        if (llmGoo.Value.CurrentModel != null)
        {
            _llmConnected = true;
        }

        if (!DA.GetData(1, ref convGoo) || convGoo?.Value == null)
        {
            return;
        }

        // consume the Shift+Enter trigger from Prompt; reset immediately so it only fires once
        bool triggered = convGoo!.Value.Trigger;
        convGoo.Value.Trigger = false;

        if (!DA.GetData(2, ref _autoFix))
        {
            return;
        }

        if (!DA.GetData(3, ref _maxAutoFixAttemps))
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

        DA.SetData(0, _debugLog);

        if (triggered && (_pendingRequest == null || _pendingRequest.IsCompleted) && !_waitingForAutoFix)
        {
            _isBusy = true;
            _pendingRequest = SendAsync(convGoo.Value, llmGoo.Value, ZooidComponent, 0);
        }
    }

    /// <summary>
    /// Assigns the custom <see cref="ComposerAttrib"/> attribute class to this component.
    /// </summary>
    public override void CreateAttributes() => m_attributes = new ComposerAttrib(this);

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

        // subscribe to deletion events so we can clean up the ZOOID link if it gets deleted
        document.ObjectsDeleted += OnDocumentObjectsDeleted;

        if (ZooidGuid != Guid.Empty)
        {
            document.ScheduleSolution(10, ReconnectZooid);
        }
    }

    /// <summary>
    /// Unsubscribes from document events when this component is removed, preventing memory leaks.
    /// </summary>
    /// <param name="document">The document this component was removed from.</param>
    public override void RemovedFromDocument(GH_Document document)
    {
        document.ObjectsDeleted -= OnDocumentObjectsDeleted;
        base.RemovedFromDocument(document);
    }

    /// <summary>
    /// Appends a Disconnect Zooid item to the standard component context menu.
    /// </summary>
    /// <param name="menu">The context menu being built.</param>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);

        var item = Menu_AppendItem(menu, "Disconnect Zooid", OnDisconnectZooid, ZooidComponent != null);
        item.ToolTipText = "Remove the link between this COMPOSER and its ZOOID.";
    }

    // removes the reference zooid when removed via the right click menu
    private void OnDisconnectZooid(object sender, EventArgs e)
    {
        ZooidComponent = null;
        ZooidGuid = Guid.Empty;
        OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(true));
    }

    // LLM REQUESTS METHODS ======================================================================================================

    // Sends the conversation to the LLM. attempt=0 is the initial request; attempt=1,2,… are
    // successive auto-fix passes. Each pass appends the error context as a new user turn, calls
    // the LLM, and recursively schedules the next pass if errors remain.
    private async Task SendAsync(Conversation conversation, LlmProvider llm, ZooidBase zooid, int attempt)
    {
        if (attempt == 0)
        {
            Message = "Calling API";
            _debugLog = string.Empty;
        }
        else
        {
            Message = $"Fixing ({attempt}/{_maxAutoFixAttemps})";

            // append the error context as a new user turn — conversation already holds prior script as assistant turn
            var fixMessage = zooid.GetFixMessage();
            if (fixMessage == null)
            {
                _isBusy = false;
                return;
            }

            // reinject user param constraints so the LLM is reminded of exact names on every fix attempt
            var paramPrompt = zooid.UserParamsPrompt();
            var fullFix = string.IsNullOrEmpty(paramPrompt) ? fixMessage : fixMessage + "\n\n" + paramPrompt;
            AppendDebugEntry($"=== Fix message sent ({attempt}/{_maxAutoFixAttemps}) ===", fullFix);
            conversation.AddUserMessage(fullFix, true);
        }

        // Initial call injects params without mutating the shared conversation;
        // fix calls already have them baked into the latest user turn.
        var history = attempt == 0
            ? PromptHelpers.AppendParamsToLastMessage(conversation.LlmMessages, zooid.UserParamsPrompt())
            : conversation.LlmMessages;

        try
        {
            var rawResult = await llm.SendConversationAsync(zooid.FormatPrompt, history);

            zooid.ApplyLlmResponse(rawResult);
            conversation.AddAssistantMessage(rawResult, zooid.LastStatusMessage);
            AppendDebugEntry(attempt == 0 ? "=== Response ===" : $"=== Fix response ({attempt}/{_maxAutoFixAttemps}) ===", rawResult);

            Message = "Done";
            OnPingDocument()?.ScheduleSolution(1, _ => { }); // triggers canvas redraw so Prompt re-renders history
            ExpireSolution(true);

            if (_autoFix && attempt < _maxAutoFixAttemps && ShouldAutoFix(zooid))
            {
                _waitingForAutoFix = true;
                OnPingDocument()?.ScheduleSolution(1500, _ =>
                {
                    _waitingForAutoFix = false;
                    _pendingRequest = SendAsync(conversation, llm, zooid, attempt + 1);
                });
            }
            else
            {
                _isBusy = false;
            }
        }
        catch (Exception ex)
        {
            _errorMsg = ex.InnerException?.Message ?? ex.Message;
            AppendDebugEntry(attempt == 0 ? "=== Error ===" : "=== Fix error ===", _errorMsg);
            Message = attempt == 0 ? "Error" : "Fix failed";
            _isBusy = false;
            ExpireSolution(true);
        }
    }

    // DETERMINES WHETHER OR NOT AN AUTOFIX SHOULD BE ATTEMPTED
    private static bool ShouldAutoFix(ZooidBase zooid)
    {
        // can't really fix components that aren't fully connected
        bool allInputsUnconnected = zooid.Params.Input.Count > 0 && zooid.Params.Input.All(p => p.Sources.Count == 0);
        return zooid.GetFixMessage() != null && !allInputsUnconnected;
    }

    // if the linked ZOOID is deleted from the canvas, clear the reference so the wire disappears
    private void OnDocumentObjectsDeleted(object sender, GH_DocObjectEventArgs e)
    {
        if (ZooidGuid != Guid.Empty && e.Objects.Any(o => o.InstanceGuid == ZooidGuid))
        {
            ZooidComponent = null;
            ZooidGuid = Guid.Empty;
            ExpireSolution(true);
        }
    }

    // Prepends a labelled entry to the debug log so the newest entry is always at the top.
    private void AppendDebugEntry(string header, string? body)
    {
        var entry = string.IsNullOrEmpty(body) ? header : $"{header}\n{body}";
        _debugLog = string.IsNullOrEmpty(_debugLog) ? entry : entry + "\n\n" + _debugLog;
    }

    // reconnects zooid - call from on opening event on saved document
    private void ReconnectZooid(GH_Document doc)
    {
        var obj = doc.FindObject(ZooidGuid, true);
        if (obj is ZooidBase zooid)
        {
            ZooidComponent = zooid;
        }
    }
}
