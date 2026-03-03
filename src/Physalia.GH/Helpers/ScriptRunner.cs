using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.Core.Prompts;
using Physalia.GH.RunPython;
using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Execution;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.GH.Helpers;

public class ScriptRunner
{
    private bool _pythonLoaded;

    /// <summary>
    /// Executes the Python 3 script from the LLM response using RhinoCode.RunScript.
    /// Collects values from the component's dynamic input parameters, unwraps them
    /// from their Grasshopper wrapper types, passes them into a RunContext, runs the
    /// script, and writes the results back to the dynamic output parameters.
    /// </summary>
    /// <param name="DA">The data access object for reading inputs and writing outputs.</param>
    /// <param name="response">The parsed LLM response containing the script and parameter definitions.</param>
    /// <param name="fixedInputCount">The number of permanent input parameters to skip when reading dynamic inputs.</param>
    /// <param name="fixedOutputCount">The number of permanent output parameters to skip when writing dynamic outputs.</param>
    public void Execute(
        IGH_DataAccess DA,
        ScriptResponse response,
        int fixedInputCount,
        int fixedOutputCount)
    {
        EnsurePythonLoaded();

        var runCtx = new RunContext
        {
            AutoApplyParams = true
        };

        CollectInputs(DA, response, fixedInputCount, runCtx);
        RegisterOutputs(response, runCtx);

        string fullScript = "#! python 3\n" + response.Script;
        RhinoCode.RunScript(fullScript, runCtx);

        SetOutputs(DA, response, fixedOutputCount, runCtx);
    }

    private void EnsurePythonLoaded()
    {
        if (_pythonLoaded) return;
        LanguageHelpers.LoadPython3();
        _pythonLoaded = true;
    }

    private static void CollectInputs(
        IGH_DataAccess DA,
        ScriptResponse response,
        int fixedInputCount,
        RunContext rhinoRunContext)
    {
        for (int i = 0; i < response.Inputs.Count; i++)
        {
            var inputDef = response.Inputs[i];
            int paramIndex = fixedInputCount + i;

            if (inputDef.Access?.ToLowerInvariant() == "list")
            {
                var list = new List<object>();
                if (DA.GetDataList(paramIndex, list))
                {
                    var unwrapped = list.Select(UnwrapGhType).ToList();
                    rhinoRunContext.Inputs[inputDef.Name] = unwrapped;
                }
            }
            else
            {
                object value = null;
                if (DA.GetData(paramIndex, ref value))
                {
                    rhinoRunContext.Inputs[inputDef.Name] = UnwrapGhType(value);
                }
            }
        }
    }

    private static void RegisterOutputs(ScriptResponse response, RunContext runCtx)
    {
        foreach (var outputDef in response.Outputs)
        {
            runCtx.Outputs[outputDef.Name] = default(object);
        }
    }

    private static void SetOutputs(
        IGH_DataAccess DA,
        ScriptResponse response,
        int fixedOutputCount,
        RunContext runCtx)
    {
        for (int i = 0; i < response.Outputs.Count; i++)
        {
            var outputDef = response.Outputs[i];
            int paramIndex = fixedOutputCount + i;

            var result = runCtx.Outputs.Get<object>(outputDef.Name);

            if (result is System.Collections.IList resultList)
            {
                DA.SetDataList(paramIndex, resultList);
            }
            else
            {
                DA.SetData(paramIndex, result);
            }
        }
    }

    private static object UnwrapGhType(object value)
    {
        if (value is IGH_Goo goo)
        {
            return goo.ScriptVariable();
        }
        return value;
    }
}