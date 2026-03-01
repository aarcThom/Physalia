using Grasshopper.Kernel;
using System;
using Rhino.Runtime.Code;
using Rhino.Runtime.Code.Execution;

namespace Physalia.GH
{
    public class Physalia : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public Physalia()
          : base("Physalia Editor", "Phy",
            "An AI assisted code editor for Grasshopper",
            "Physalia", "Core")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Index 0: For our calculated_value
            pManager.AddGenericParameter("Result", "R", "The calculated math value", GH_ParamAccess.item);

            // Index 1: For our python_version
            pManager.AddTextParameter("Version", "V", "The Python engine version", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. The ultra-strict Rhino 8 shebang (notice the space!)
            string pythonCode =
                "#! python 3" + Environment.NewLine +
                "import sys" + Environment.NewLine +
                "import math" + Environment.NewLine +
                Environment.NewLine +
                "calculated_value = input_number * math.pi" + Environment.NewLine +
                "python_version = sys.version" + Environment.NewLine;

            // 2. Set up our test input variable
            int userNumber = 10;

            // 3. Create the Execution Context
            var ctx = new Rhino.Runtime.Code.Execution.RunContext
            {
                AutoApplyParams = true,
                Inputs = { ["input_number"] = userNumber },
                Outputs = { ["calculated_value"] = null, ["python_version"] = null }
            };

            try
            {
                // 4. Create and run the code using the single, proven argument!
                var code = Rhino.Runtime.Code.RhinoCode.CreateCode(pythonCode);
                code.Run(ctx);

                // 5. Extract the outputs back into C# variables
                if (ctx.Outputs.TryGet("calculated_value", out object resultVal))
                {
                    DA.SetData(0, resultVal); // Output back to GH
                }

                if (ctx.Outputs.TryGet("python_version", out object versionStr))
                {
                    DA.SetData(1, versionStr); // Output back to GH
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Python Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("24AA372D-00AD-4350-9B1D-AE180549C4D3");
    }
}