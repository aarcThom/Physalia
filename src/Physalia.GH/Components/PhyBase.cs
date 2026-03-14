using Grasshopper.Kernel;
using System.Drawing;
using System.Reflection;

namespace SiteReader.Components
{
    public abstract class PhyBase : GH_Component
    {
        //grabbing embedded resources
        protected readonly Assembly GHAssembly = Assembly.GetExecutingAssembly();

        protected string IconPath;

        protected PhyBase(string name, string nickname, string description, string subCategory)
            : base(name, nickname, description, "Physalia", subCategory)
        {
        }

        /// <summary>
        /// Provides an Icon for the component. Defaults to generic icon if none provided.
        /// </summary>
        protected override Bitmap Icon
        {
            get
            {
                if (IconPath == null)
                {
                    IconPath = "Physalia.GH.Resources.brain.png";
                }

                System.IO.Stream stream = GHAssembly.GetManifestResourceStream(IconPath);
                return new Bitmap(stream);
            }
        }
    }
}