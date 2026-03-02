using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.Core.Prompts;
using Physalia.GH.Helpers;
using System;
using System.Linq;

namespace Physalia.GH.Components;

public class PhysaliaComponent : GH_Component, IGH_VariableParameterComponent
{
    private readonly ApiCaller _apiCaller = new();
    private readonly ScriptRunner _scriptRunner = new();

    private ScriptResponse? _lastResponse;
    private string? _lastPrompt;
    private string? _errorMsg;

    private const int FixedInputCount = 4;
    private const int FixedOutputCount = 1;

    public PhysaliaComponent()
        : base(
            "Physalia AI",
            "PhysAI",
            "Generate a Python 3 script from a natural language prompt",
            "Physalia",
            "AI")
    { }

    public override void CreateAttributes()
    {
        m_attributes = new PhysaliaAttributes(this);
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Prompt", "P", "Describe what the script should do", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Send", "S", "Click to send prompt to the LLM", GH_ParamAccess.item, false);
        pManager.AddTextParameter("Keys Path", "K", "Path to physaliaKeys.json", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Run", "R", "Set to true to execute the Python script", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Script", "Sc", "The generated Python script", GH_ParamAccess.item);
    }

    public void OpenScriptEditor()
    {
        if (_lastResponse == null) return;

        var inputNames = _lastResponse.Inputs.Select(i => i.Name).ToList();
        var dialog = new ScriptEditorDialog(_lastResponse.Script, inputNames);
        var result = dialog.ShowModal(Grasshopper.Instances.EtoDocumentEditor);

        if (result != null)
        {
            _lastResponse.Script = result;
            Message = "Edited";
            ExpireSolution(true);
        }
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string prompt = null;
        bool send = false;
        string keysPath = null;
        bool run = false;

        if (!DA.GetData(0, ref prompt)) return;
        if (!DA.GetData(1, ref send)) return;
        if (!DA.GetData(2, ref keysPath)) return;
        if (!DA.GetData(3, ref run)) return;

        if (_errorMsg != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _errorMsg);
            _errorMsg = null;
        }

        if (_lastResponse != null)
        {
            DA.SetData(0, _lastResponse.Script);
        }

        // --- SEND ---
        if (send && !_apiCaller.IsWaiting && prompt != _lastPrompt)
        {
            Message = "Calling API...";

            _apiCaller.SendAsync(prompt, keysPath,
                onSuccess: result =>
                {
                    _lastResponse = result;
                    _lastPrompt = prompt;
                    ParameterBuilder.Rebuild(this, result, FixedInputCount, FixedOutputCount);
                    Message = "Done";
                    ExpireSolution(true);
                },
                onError: error =>
                {
                    _errorMsg = error;
                    Message = "Error";
                    ExpireSolution(true);
                });

            return;
        }

        // --- RUN ---
        if (run && _lastResponse != null)
        {
            try
            {
                _scriptRunner.Execute(DA, _lastResponse, FixedInputCount, FixedOutputCount);
                Message = "Done";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Script execution failed: {ex.Message}");
                Message = "Script Error";
            }
        }
    }

    // --- IGH_VariableParameterComponent implementation ---

    public bool CanInsertParameter(GH_ParameterSide side, int index) => false;
    public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;
    public IGH_Param CreateParameter(GH_ParameterSide side, int index) => new Param_GenericObject();
    public bool DestroyParameter(GH_ParameterSide side, int index) => true;
    public void VariableParameterMaintenance() { }

    public override Guid ComponentGuid => new("37DF1EAA-0867-4487-AF27-0665C34DBB26");
}