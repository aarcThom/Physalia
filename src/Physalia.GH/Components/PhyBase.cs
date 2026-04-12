using Grasshopper.Kernel;
using System.Drawing;
using System.Reflection;

namespace Physalia.GH.Components;

public abstract class PhyBase : GH_Component
{
    //grabbing embedded resources
    protected readonly Assembly GHAssembly = Assembly.GetExecutingAssembly();

    protected string IconPath;
    private Bitmap? _iconCache;

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
            if (_iconCache != null)
            {
                return _iconCache;
            }

            if (IconPath == null)
            {
                IconPath = "Physalia.GH.Resources.brain.png";
            }

            using System.IO.Stream? stream = GHAssembly.GetManifestResourceStream(IconPath);
            if (stream == null)
            {
                // Fallback to an empty bitmap so GH doesn't crash if the resource isn't found.
                _iconCache = new Bitmap(24, 24);
                return _iconCache;
            }

            _iconCache = new Bitmap(stream);
            return _iconCache;
        }
    }
}
