// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// A transmitter's link to the object on the user's canvas it writes into: the target's id and its
/// persistence, the link/unlink gestures (harness-grip drop and right-click picker alike), where the
/// settled wire lands, and resolving the id back to a live object on the host document.
///
/// <para>Composed rather than inherited, because the two kinds of transmitter that need it sit on
/// different bases: <see cref="ScriptTransmitterBase"/> is a routing component driven by signals,
/// while <see cref="HarnessOut"/> is a plain passthrough with no signal lifecycle at all. This
/// is the one implementation of "how a transmitter is linked", and every gesture goes through it.</para>
/// </summary>
internal sealed class TransmitterLink
{
    // Distance below the target the settled wire ends at, when it has no input grip of its own. It is
    // exactly the arrowhead's height because the head is drawn FORWARD of the wire end — the end is
    // the base centre, not the tip — so dropping the wire by that much lands the tip on the node's
    // bottom edge with no gap under it. Taken from the head rather than typed as a number, or
    // resizing the head would silently open one.
    private static readonly float _wireTipDrop = TriangleArrowHead.Default.Height;

    // How far outside a node's bounds a drop still counts as landing on it. Grasshopper draws a
    // param's grip just off the capsule edge, so an exact bounds test misses the very spot the user
    // aims at when connecting like a normal wire.
    private const float GripReach = 8f;

    private const string GuidKey = "LinkedGuid";

    private readonly IGH_Component _owner;
    private readonly string _menuNoun;
    private readonly string _kindNoun;
    private readonly Func<IGH_DocumentObject, bool> _isTarget;

    private Guid _guid = Guid.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransmitterLink"/> class.
    /// </summary>
    /// <param name="owner">The transmitter that owns the link — used for undo, expiry, and host lookup.</param>
    /// <param name="menuNoun">What the right-click items call the target ("Script Component").</param>
    /// <param name="kindNoun">What messages call the target ("Python Script", "Panel or text input").</param>
    /// <param name="isTarget">Whether a document object is a valid target.</param>
    internal TransmitterLink(
        IGH_Component owner,
        string menuNoun,
        string kindNoun,
        Func<IGH_DocumentObject, bool> isTarget)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(isTarget);

        _owner = owner;
        _menuNoun = menuNoun;
        _kindNoun = kindNoun;
        _isTarget = isTarget;
    }

    /// <summary>Gets the linked target's InstanceGuid, or <see cref="System.Guid.Empty"/> when unlinked.</summary>
    internal Guid Guid => _guid;

    /// <summary>
    /// Gets or sets the callback run when the link is repointed through <see cref="Set"/>. The owner
    /// uses it to drop state that was about the OLD target.
    /// </summary>
    internal Action? Changed { get; set; }

    /// <summary>
    /// Points the link at a target with no undo record and no re-solve — the raw setter behind a
    /// transmitter's public LinkTo/Unlink.
    /// </summary>
    /// <param name="guid">The target's id, or <see cref="System.Guid.Empty"/> to unlink.</param>
    internal void Assign(Guid guid) => _guid = guid;

    /// <summary>
    /// Applies a link change made by the user: records undo so the pick can be reversed, tells the
    /// owner, and re-solves so the target and anything watching it pick the change up.
    /// </summary>
    /// <param name="guid">The target's id, or <see cref="System.Guid.Empty"/> to unlink.</param>
    internal void Set(Guid guid)
    {
        _owner.RecordUndoEvent(guid == Guid.Empty ? $"Unlink {_menuNoun}" : $"Link {_menuNoun}");
        _guid = guid;
        Changed?.Invoke();
        _owner.ExpireSolution(true);
    }

    /// <summary>
    /// Returns where the settled wire lands: just under the linked target, or nothing when unlinked.
    /// </summary>
    /// <param name="hostDocument">The user's canvas, where the target lives.</param>
    /// <returns>The settled wire end points, in canvas coordinates.</returns>
    internal IEnumerable<PointF> Endpoints(GH_Document hostDocument)
    {
        // topLevelOnly: false — a target may be a component's input parameter, which is not in the
        // document's own object list.
        if (_guid == Guid.Empty || hostDocument.FindObject(_guid, false) is not { } target)
        {
            yield break;
        }

        // Land on the target's input grip when it has one, so the wire arrives exactly where a
        // Grasshopper wire would. Whole components (the script transmitters' targets) have none, and
        // take the tip just under the node instead.
        if (target.Attributes is { HasInputGrip: true } attributes)
        {
            yield return attributes.InputGrip;
            yield break;
        }

        RectangleF b = target.Attributes.Bounds;
        yield return new PointF(b.Left + (b.Width / 2f), b.Bottom + _wireTipDrop);
    }

    /// <summary>
    /// Commits a drop from the harness proxy's grip: links whatever valid target it landed on, or
    /// unlinks when the drag carried the Ctrl intent.
    /// </summary>
    /// <param name="hostDocument">The user's canvas, where the drop landed.</param>
    /// <param name="dropPoint">The drop point in canvas coordinates.</param>
    /// <param name="ctrl">Whether the drag carried the disconnect (Ctrl) intent.</param>
    /// <param name="refine">
    /// Optional: given the node the drop landed on, returns the object to actually link — a
    /// component's input parameter rather than the component, say. Defaults to the node itself when
    /// valid.
    /// </param>
    internal void HandleDrop(
        GH_Document hostDocument,
        PointF dropPoint,
        bool ctrl,
        Func<IGH_DocumentObject, PointF, IGH_DocumentObject?>? refine = null)
    {
        // Nodes proper first, then a second sweep with the grip reach, so a drop that is inside one
        // node and merely near another always belongs to the one it is actually on.
        IGH_DocumentObject? hit = FindNodeAt(hostDocument, dropPoint, 0f)
            ?? FindNodeAt(hostDocument, dropPoint, GripReach);

        if (hit is null)
        {
            return; // empty canvas: a drop with no target is simply nothing, never a placement
        }

        IGH_DocumentObject? target = refine is null
            ? (_isTarget(hit) ? hit : null)
            : refine(hit, dropPoint);

        if (target is not null)
        {
            Set(ctrl ? Guid.Empty : target.InstanceGuid);
        }
    }

    // The node under a canvas point, allowing the given slack around each one's bounds.
    private static IGH_DocumentObject? FindNodeAt(GH_Document document, PointF point, float slack)
    {
        foreach (IGH_DocumentObject obj in document.Objects)
        {
            RectangleF bounds = slack > 0f
                ? RectangleF.Inflate(obj.Attributes.Bounds, slack, slack)
                : obj.Attributes.Bounds;

            if (bounds.Contains(point))
            {
                return obj;
            }
        }

        return null;
    }

    /// <summary>
    /// Appends the link picker and the unlink item to a transmitter's right-click menu. A transmitter
    /// normally lives inside a harness while its target sits on the user's canvas, and a drag cannot
    /// cross two canvases — so the link must also be pickable from a list.
    /// </summary>
    /// <param name="menu">The menu being built.</param>
    internal void AppendMenuItems(ToolStripDropDown menu)
    {
        ToolStripMenuItem picker = GH_DocumentObject.Menu_AppendItem(menu, $"Link to {_menuNoun}");

        List<IGH_DocumentObject> candidates = PhyDocuments.Host(_owner) is { } host
            ? host.Objects.Where(_isTarget).ToList()
            : new List<IGH_DocumentObject>();

        if (candidates.Count == 0)
        {
            GH_DocumentObject.Menu_AppendItem(picker.DropDown, $"No {_kindNoun} on the canvas", (_, _) => { }, false);
        }
        else
        {
            foreach (IGH_DocumentObject candidate in candidates)
            {
                Guid guid = candidate.InstanceGuid;
                GH_DocumentObject.Menu_AppendItem(
                    picker.DropDown,
                    $"{candidate.NickName}  ({guid.ToString()[..8]})",
                    (_, _) => Set(guid),
                    enabled: true,
                    @checked: guid == _guid);
            }
        }

        GH_DocumentObject.Menu_AppendItem(menu, $"Unlink {_menuNoun}", (_, _) => Set(Guid.Empty), _guid != Guid.Empty);
    }

    /// <summary>
    /// Resolves the linked object on the host canvas, or returns null with a message when no valid
    /// target is linked.
    /// </summary>
    /// <param name="error">A human-readable reason when resolution fails; otherwise null.</param>
    /// <returns>The linked document object, or null.</returns>
    internal IGH_DocumentObject? Resolve(out string? error)
    {
        error = null;

        if (_guid == Guid.Empty)
        {
            error = $"No {_kindNoun} is linked. Drag from this harness's grip onto the target, or use "
                + $"\"Link to {_menuNoun}\" on this node's right-click menu.";
            return null;
        }

        // The target lives on the user's canvas, not in the harness this transmitter runs in.
        IGH_DocumentObject? linked = PhyDocuments.Host(_owner)?.FindObject(_guid, false);
        if (linked is null || !_isTarget(linked))
        {
            error = $"The linked object is gone, or is not a {_kindNoun}.";
            return null;
        }

        return linked;
    }

    /// <summary>Writes the link into the component's archive.</summary>
    /// <param name="writer">The archive writer.</param>
    internal void Write(GH_IWriter writer) => writer.SetGuid(GuidKey, _guid);

    /// <summary>Reads the link back from the component's archive.</summary>
    /// <param name="reader">The archive reader.</param>
    internal void Read(GH_IReader reader)
    {
        if (reader.ItemExists(GuidKey))
        {
            _guid = reader.GetGuid(GuidKey);
        }
    }

    /// <summary>
    /// Re-points the link when instance ids are re-issued (a preset load). The target normally lives
    /// on the USER'S CANVAS, so its id is absent from the mapping and the link is left exactly as it
    /// was — which is what keeps a preset's transmitter pointing at nothing rather than at something
    /// arbitrary. The lookup is still made, for the day a transmitter targets a peer.
    /// </summary>
    /// <param name="replacements">Old id to new id, for the objects being re-issued.</param>
    internal void Remap(IReadOnlyDictionary<Guid, Guid> replacements)
    {
        if (replacements.TryGetValue(_guid, out Guid replacement))
        {
            _guid = replacement;
        }
    }
}
