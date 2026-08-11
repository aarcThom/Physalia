// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace Physalia.GH.Components;

/// <summary>
/// Base for signal-driven transmitters that push into an EXISTING component on the user's canvas — a
/// script component of one language or another — rather than placing new ones. Being linked is
/// delegated to a <see cref="TransmitterLink"/>: the target's id and persistence, the link/unlink
/// gestures, the settled wire, and resolving the id back to a live object.
///
/// <para>A subclass says only what counts as a valid target (<see cref="IsLinkTarget"/>), what to
/// call that kind of component in a message (<see cref="TargetKind"/>), and what pushing means. That
/// is the seam the Python, IronPython and C# transmitters differ across. A transmitter that is NOT
/// signal-driven composes the same <see cref="TransmitterLink"/> directly instead — see
/// <see cref="TextTransmitter"/>.</para>
/// </summary>
public abstract class ScriptTransmitterBase : TransmitterComponentBase, IGuidLinked
{
    private TransmitterLink? _link;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptTransmitterBase"/> class.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected ScriptTransmitterBase(string name, string nickname, string description)
        : base(name, nickname, description)
    {
    }

    /// <summary>
    /// Gets the InstanceGuid of the linked target component, or <see cref="Guid.Empty"/> if unlinked.
    /// </summary>
    public Guid LinkedGuid => Link.Guid;

    /// <summary>
    /// Gets what this transmitter's target is called in messages to the user and the model —
    /// "Python Script", "C# Script", and so on.
    /// </summary>
    protected abstract string TargetKind { get; }

    // Built on first use rather than in the constructor: it is wired from TargetKind and
    // IsLinkTarget, which a subclass overrides.
    private TransmitterLink Link
    {
        get
        {
            if (_link is null)
            {
                _link = new TransmitterLink(this, "Script Component", TargetKind, IsLinkTarget);
                _link.Changed = OnLinkChanged;
            }

            return _link;
        }
    }

    /// <summary>
    /// Whether a document object on the host canvas is a component this transmitter can push into.
    /// </summary>
    /// <param name="candidate">An object on the user's canvas.</param>
    /// <returns>true when it is a valid link target.</returns>
    protected abstract bool IsLinkTarget(IGH_DocumentObject candidate);

    /// <summary>
    /// Links this component to a target component.
    /// Called from the right-click picker, and by the harness proxy's delegated arrow on drop.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the component to link.</param>
    public void LinkTo(Guid guid) => Link.Assign(guid);

    /// <summary>
    /// Removes the current link. Does not modify the previously-linked component's code.
    /// Called from the right-click picker, and by the harness proxy's delegated arrow on Ctrl+drop.
    /// </summary>
    public void Unlink() => Link.Assign(Guid.Empty);

    /// <inheritdoc/>
    /// <remarks>The wire lands just under the linked component.</remarks>
    public override IEnumerable<PointF> GetArrowEndpoints(GH_Document hostDocument) =>
        Link.Endpoints(hostDocument);

    /// <inheritdoc/>
    /// <remarks>Links the component under the drop point; Ctrl unlinks instead.</remarks>
    public override void HandleDrop(GH_Document hostDocument, PointF dropPoint, bool ctrl) =>
        Link.HandleDrop(hostDocument, dropPoint, ctrl);

    /// <inheritdoc/>
    /// <remarks>Offers the grip link as a menu, for when the target cannot be reached by a drag.</remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Link.AppendMenuItems(menu);
    }

    /// <inheritdoc/>
    void IGuidLinked.RemapLinks(IReadOnlyDictionary<Guid, Guid> replacements) => Link.Remap(replacements);

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        Link.Write(writer);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        Link.Read(reader);
        return base.Read(reader);
    }

    /// <summary>
    /// Applies a link change from the menu or a drop, with undo and a re-solve.
    /// </summary>
    /// <param name="guid">The target to link, or <see cref="Guid.Empty"/> to unlink.</param>
    protected void SetLink(Guid guid) => Link.Set(guid);

    /// <summary>
    /// Resolves the linked component on the host canvas, or returns null with a message when no
    /// valid target is linked.
    /// </summary>
    /// <param name="error">A human-readable reason when resolution fails; otherwise null.</param>
    /// <returns>The linked document object, or null.</returns>
    protected IGH_DocumentObject? ResolveTarget(out string? error) => Link.Resolve(out error);

    /// <summary>
    /// Called after the link is repointed. Override to drop any state that was about the OLD target —
    /// a transmitter that skips a push because "the target already has this" must forget that, or the
    /// newly linked component is left empty. Does nothing by default.
    /// </summary>
    protected virtual void OnLinkChanged()
    {
    }
}
