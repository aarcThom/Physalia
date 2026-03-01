using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace Physalia.GH
{
    public class Physalia_GHInfo : GH_AssemblyInfo
    {
        public override string Name => "Physalia";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("d0040934-d7b4-464e-9aaf-38f5e9634275");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}