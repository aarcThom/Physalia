// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.GH.Generation;
using System.Drawing;
using System.Reflection;

namespace Physalia.GH.Components;

public abstract class PhyBase : GH_Component
{
    //grabbing embedded resources
    protected readonly Assembly GHAssembly = Assembly.GetExecutingAssembly();
    protected string? IconPath;
    private Bitmap? _iconCache;
    private bool _restored;

    protected PhyBase(string name, string nickname, string description, string subCategory)
        : base(name, nickname, description, "Physalia", subCategory)
    {
    }

    /// <summary>
    /// Gets a value indicating whether this component was read out of a FILE rather than placed
    /// from the ribbon.
    ///
    /// <para>Grasshopper deserializes an object by emitting it, calling <see cref="Read"/> on it,
    /// and only then adding it to the document — so by the time <c>AddedToDocument</c> runs, this
    /// says which of the two happened. A fresh placement never gets a <c>Read</c>.</para>
    ///
    /// <para>It exists for <see cref="AutoPlacePicker"/>, and anything else that wants to do
    /// something once when a component is first placed rather than every time the file is
    /// opened.</para>
    /// </summary>
    protected bool WasRestored => _restored;

    /// <inheritdoc/>
    /// <remarks>
    /// No <see cref="PhyBase"/> subclass overrides this today. One that does MUST call
    /// <c>base.Read</c>, or the component will look freshly placed every time its file is opened
    /// and <see cref="AutoPlacePicker"/> will start adding a Picker on every load.
    /// </remarks>
    public override bool Read(GH_IReader reader)
    {
        _restored = true;
        return base.Read(reader);
    }

    /// <summary>
    /// Places a <see cref="Picker"/> on an input and wires it up — but only for a component the
    /// user has just dropped on the canvas.
    ///
    /// <para>Call it from <c>AddedToDocument</c>. It is the single place the three reasons NOT to
    /// auto-place one live, and the third is the one that used to be missing:</para>
    ///
    /// <list type="bullet">
    /// <item><description>a GhJSON import is placing the component, and the definition being
    /// imported says what is wired to what;</description></item>
    /// <item><description>the input already has a source, so there is nothing to choose;</description></item>
    /// <item><description>the component came out of a FILE, in which case its Pickers were saved
    /// alongside it and anything missing is missing ON PURPOSE.</description></item>
    /// </list>
    ///
    /// <para>Without that last one, an input deliberately left empty grew a fresh Picker every time
    /// the file was opened — and a fresh Picker with no saved choice snaps to the first entry in its
    /// list on its second solve. Measured on a plain conversational preset, which reloaded with the
    /// 11,900-character C# Script preamble silently folded into its system prompt. Deleting the
    /// Picker was the documented workaround and it did not survive a save.</para>
    /// </summary>
    /// <param name="document">The document the component was added to.</param>
    /// <param name="paramIndex">Index of the input to place the Picker on.</param>
    /// <param name="xOffset">Horizontal offset from this component's pivot (negative is left).</param>
    /// <param name="yOffset">Vertical offset from this component's pivot (positive is down).</param>
    /// <returns>The Picker that was placed, or null when none was.</returns>
    protected Picker? AutoPlacePicker(
        GH_Document document,
        int paramIndex,
        float xOffset = -200f,
        float yOffset = 0f)
    {
        if (GhJsonBridge.IsImporting || WasRestored)
        {
            return null;
        }

        if (Params.Input[paramIndex].SourceCount > 0)
        {
            return null;
        }

        return ComponentHelpers.PickerAdd(this, document, paramIndex, xOffset, yOffset);
    }

    /// <summary>
    /// Provides the default Physalia attributes. Components with bespoke drawing override this.
    /// </summary>
    public override void CreateAttributes()
    {
        m_attributes = new Attributes.PhyComponentAttributes(this);
    }

    /// <summary>
    /// Provides an Icon for the component. Resolves the embedded resource named
    /// after the concrete component type (e.g. <c>SchemaValidator</c> → <c>SchemaValidator.png</c>),
    /// honouring an explicit <see cref="IconPath"/> override if one is set, and
    /// falling back to the generic brain icon when no matching resource exists.
    /// </summary>
    protected override Bitmap Icon
    {
        get
        {
            if (_iconCache != null)
            {
                return _iconCache;
            }

            // Explicit override wins; otherwise derive the resource name from the runtime type.
            string resourceName = IconPath ?? $"Physalia.GH.Resources.{GetType().Name}.png";

            using System.IO.Stream? stream =
                GHAssembly.GetManifestResourceStream(resourceName)
                ?? GHAssembly.GetManifestResourceStream("Physalia.GH.Resources.brain.png");

            // Fallback to an empty bitmap so GH doesn't crash if no resource is found.
            _iconCache = stream != null ? new Bitmap(stream) : new Bitmap(24, 24);
            return _iconCache;
        }
    }
}
