using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Physalia.Core;
using Physalia.Core.Config;
using Physalia.Core.Parsing;
using Physalia.Core.Prompts;
using Physalia.Core.Providers;
using System;
using System.Threading.Tasks;

namespace Physalia.GH.Components;

public class PhysaliaComponent : GH_Component, IGH_VariableParameterComponent
{
    private ScriptResponse? _lastResponse;
    private string? _lastScript;
    private string? _lastPrompt;
    private bool _waiting;
    private string? _errorMsg;

    public PhysaliaComponent()
        : base(
            "Physalia AI",
            "PhysAI",
            "Generate a Python 3 script from a natural language prompt",
            "Physalia",
            "AI")
    { }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Prompt", "P", "Describe what the script should do", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Send", "S", "Click to send prompt to the LLM", GH_ParamAccess.item, false);
        pManager.AddTextParameter("Keys Path", "K", "Path to physaliaKeys.json", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Script", "Sc", "The generated Python script", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string prompt = null;
        bool send = false;
        string keysPath = null;

        if (!DA.GetData(0, ref prompt)) return;
        if (!DA.GetData(1, ref send)) return;
        if (!DA.GetData(2, ref keysPath)) return;

        // Show any error from the background thread
        if (_errorMsg != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _errorMsg);
            _errorMsg = null;
        }

        // Always output the last script if we have one
        if (_lastScript != null)
        {
            DA.SetData(0, _lastScript);
        }

        // Only call the API when Send is true
        if (!send) return;

        // Don't send while already waiting
        if (_waiting) return;

        // Don't re-send the exact same prompt
        if (prompt == _lastPrompt) return;

        Message = "Calling API...";
        _waiting = true;

        // Capture values for the background thread
        string capturedPrompt = prompt;
        string capturedKeysPath = keysPath;

        Task.Run(async () =>
        {
            try
            {
                var resolver = new ApiKeyResolver(capturedKeysPath);
                var apiKey = resolver.GetKey("Claude Code");

                var provider = new AnthropicProvider();
                var client = new PhysaliaClient(provider, apiKey);

                var result = await client.GenerateScriptAsync(capturedPrompt);

                _lastScript = result.Script;
                _lastResponse = result;
                _lastPrompt = capturedPrompt;

                Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    RebuildParameters(result);
                }));
            }
            catch (Exception ex)
            {
                _errorMsg = ex.InnerException?.Message ?? ex.Message;
                Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    _waiting = false;
                    Message = "Error";
                    ExpireSolution(true);
                }));
            }
        });
    }

    private void RebuildParameters(ScriptResponse response)
    {
        while (Params.Input.Count > 3)
        {
            Params.UnregisterInputParameter(Params.Input[Params.Input.Count - 1]);
        }

        while (Params.Output.Count > 1)
        {
            Params.UnregisterOutputParameter(Params.Output[Params.Output.Count - 1]);
        }

        foreach (var input in response.Inputs)
        {
            var param = CreateInputParam(input);
            Params.RegisterInputParam(param);
        }

        foreach (var output in response.Outputs)
        {
            var param = new Param_GenericObject
            {
                Name = output.PrettyName ?? output.Name,
                NickName = output.Name,
                Description = output.Tooltip ?? "",
                Access = GH_ParamAccess.item
            };
            Params.RegisterOutputParam(param);
        }

        _waiting = false;
        Message = "Done";
        Params.OnParametersChanged();
        ExpireSolution(true);
    }

    private static IGH_Param CreateInputParam(ParamDefinition def)
    {
        IGH_Param param = def.TypeHint?.ToLowerInvariant() switch
        {
            "int" => new Param_Integer(),
            "double" => new Param_Number(),
            "bool" => new Param_Boolean(),
            "string" => new Param_String(),
            "point3d" => new Param_Point(),
            "vector3d" => new Param_Vector(),
            "plane" => new Param_Plane(),
            "line" => new Param_Line(),
            "curve" => new Param_Curve(),
            "surface" => new Param_Surface(),
            "brep" => new Param_Brep(),
            "mesh" => new Param_Mesh(),
            "color" => new Param_Colour(),
            _ => new Param_GenericObject()
        };

        param.Name = def.PrettyName ?? def.Name;
        param.NickName = def.Name;
        param.Description = def.Tooltip ?? "";
        param.Optional = true;  // Always optional — user connects data when ready

        param.Access = def.Access?.ToLowerInvariant() switch
        {
            "list" => GH_ParamAccess.list,
            "tree" => GH_ParamAccess.tree,
            _ => GH_ParamAccess.item
        };

        return param;
    }

    public bool CanInsertParameter(GH_ParameterSide side, int index) => false;
    public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;
    public IGH_Param CreateParameter(GH_ParameterSide side, int index) => new Param_GenericObject();
    public bool DestroyParameter(GH_ParameterSide side, int index) => true;
    public void VariableParameterMaintenance() { }

    public override Guid ComponentGuid => new("37DF1EAA-0867-4487-AF27-0665C34DBB26");
}