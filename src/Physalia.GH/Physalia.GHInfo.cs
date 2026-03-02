using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace Physalia.GH
{
    public class Physalia_GHInfo : GH_AssemblyInfo
    {
        public override string Name => "Physalia";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "An open-source LLM powered GH library.";

        public override Guid Id => new Guid("862C53A2-69A1-4B56-A133-26E0BCEDE789");

        //Return a string identifying you or your company.
        public override string AuthorName => "Thomas Gaudin";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "thomas@aarc.io";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}