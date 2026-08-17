// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel;
using Physalia.GH.Harness;
using GHGroupObject = Grasshopper.Kernel.Special.GH_Group;
using GHPanel = Grasshopper.Kernel.Special.GH_Panel;

namespace Physalia.GH.Generation;

/// <summary>
/// Master-group half of the façade: every component the model places is enrolled into one canvas
/// group named "Physalia &lt;harness id&gt;" — the shared workspace between the model and ONE pipeline.
/// The id is what keeps two harnesses grounded this way apart; without it they locked onto the same
/// group and each exported the other's work as its own canvas. The group comes
/// into existence at the transmitter's placement tip (its bounds are its members'), the user can
/// drop their own components into it to bring them into the model's view, and the group-scoped
/// canvas grounding exports ONLY its contents, so a busy canvas of unrelated user work never
/// reaches the prompt. The master group itself is infrastructure: it is excluded from every canvas
/// export and from placement layout, so the model never sees it, can never target it with a group
/// op, and its box never becomes a rigid layout body swallowing all functional areas.
///
/// <para>Both frames share the plain <c>sha256-…</c> checksum form — a frame marker inside the
/// string is not an option, because the GhJSON library's ghpatch schema regex-rejects anything
/// else and its error flattening then buries the real cause under unrelated "false schema" noise
/// (observed live 2026-07-28: every stage burned two rounds on it). The patch path instead
/// resolves the frame by MATCHING: <see cref="ResolveBaseSnapshot"/> compares the carried checksum
/// against each frame's export and applies against whichever one the model actually saw.
/// Guardrails that hand the model a fresh checksum use <see cref="CurrentBaseChecksum"/>, which
/// follows the frame the Conversation Log last folded (<see cref="RecordActiveFrame"/>).</para>
/// </summary>
internal static partial class GhJsonBridge
{
    /// <summary>
    /// Stem of every master group's nickname. On its own — with no id after it — it is the LEGACY
    /// name, from when a document could hold only one master group; a harness with no group of its
    /// own adopts such a group and stamps its id onto it (see <see cref="EnsureMasterGroup"/>).
    /// </summary>
    internal const string MasterGroupBaseName = "Physalia";

    // Digits of the harness's instance id appended to the stem. The name has to be unique per HARNESS
    // (two harnesses grounded on their group used to lock onto the same one and export each other's
    // work) and stable across saves — the harness's own id is both, for free: no new persisted field,
    // and a COPIED harness, which Grasshopper gives a fresh id, correctly reads as a new pipeline
    // needing its own group instead of fighting the original for one.
    private const int MasterGroupTokenLength = 8;

    /// <summary>
    /// Nickname identifying the hint panel — the note dropped inside the master group when the group
    /// is created up front, saying what the group is for. Like the group's own name it IS the
    /// contract: a panel carrying it is Physalia infrastructure and stays out of every canvas
    /// export, so the model never sees a note addressed to the user; renaming it hands it to the
    /// user's graph, which is exactly what a rename should mean.
    /// </summary>
    internal const string HintPanelName = "Physalia";

    // What the hint panel says, and how big it is drawn.
    private const string HintPanelText = "you can add your own components to the Physalia group";
    private const float HintPanelWidth = 250f;
    private const float HintPanelHeight = 58f;
    private const float HintPanelGap = 20f;

    // Faint translucent teal, distinct from Grasshopper's default group colour so the shared
    // workspace reads as Physalia's at a glance without shouting over the model's own group colours.
    private static readonly Color MasterGroupColour = Color.FromArgb(45, 105, 155, 170);

    // Light robin egg blue — the hint panel is an invitation, not a warning, and it is the one thing
    // in the group that is neither the model's work nor the user's.
    private static readonly Color HintPanelColour = Color.FromArgb(255, 168, 226, 219);

    // Which reference frame the Conversation Log last folded, per PIPELINE: true = the model is
    // reading the group-scoped canvas state. Keyed by harness where there is one (each harness reads
    // its own frame) and by document otherwise. Session-only, weak-keyed, like the stable-id registry.
    private static readonly ConditionalWeakTable<object, StrongBox<bool>> ActiveFrames = new();

    /// <summary>
    /// The nickname of a harness's master group: the stem plus the harness's id. A user renaming the
    /// group detaches it from that harness (a fresh one is created on the next placement); naming
    /// their own group exactly this way adopts it — both are deliberate, the name IS the contract.
    /// </summary>
    /// <param name="harness">The harness owning the pipeline; null gives the legacy stem alone.</param>
    /// <returns>The group nickname to look for or create.</returns>
    internal static string MasterGroupName(HarnessComponent? harness)
        => harness is null
            ? MasterGroupBaseName
            : MasterGroupBaseName + " " + harness.InstanceGuid.ToString("N")[..MasterGroupTokenLength];

    /// <summary>
    /// True when the object is A master group — any harness's, or a legacy un-suffixed one. Used by
    /// exports and layout to treat it as invisible infrastructure rather than a model-authored
    /// functional area, which is a judgement about the KIND of group, not about whose it is.
    /// </summary>
    /// <param name="obj">The document object to test.</param>
    /// <returns>True for a group whose nickname is the stem, with or without an id after it.</returns>
    internal static bool IsMasterGroup(IGH_DocumentObject? obj)
        => obj is GHGroupObject group && IsMasterGroupName(group.NickName);

    /// <summary>
    /// True when the object is the master group's hint panel. Treated like the group itself —
    /// infrastructure, excluded from every canvas export.
    /// </summary>
    /// <param name="obj">The document object to test.</param>
    /// <returns>True for a panel nicknamed <see cref="HintPanelName"/>.</returns>
    internal static bool IsHintPanel(IGH_DocumentObject? obj)
        => obj is GHPanel panel
           && string.Equals(panel.NickName, HintPanelName, StringComparison.Ordinal);

    /// <summary>
    /// Finds a harness's master group on the document, or null when it has none yet. Another
    /// harness's group is never returned: that separation is the whole point of the id in the name.
    /// A legacy un-suffixed group is returned to any harness that has no group of its own, so an
    /// older file keeps grounding the model on what is already in it — the first harness to PLACE
    /// through it claims it by stamping its id on (see <see cref="EnsureMasterGroup"/>), and from
    /// then on the others are back to having none.
    /// </summary>
    /// <param name="doc">The document to search; null returns null.</param>
    /// <param name="harness">The harness whose group is wanted; null matches the legacy name.</param>
    /// <returns>The master group, or null.</returns>
    internal static GHGroupObject? FindMasterGroup(GH_Document? doc, HarnessComponent? harness)
    {
        if (doc is null)
        {
            return null;
        }

        string name = MasterGroupName(harness);
        GHGroupObject? legacy = null;

        foreach (GHGroupObject group in doc.Objects.OfType<GHGroupObject>())
        {
            if (string.Equals(group.NickName, name, StringComparison.Ordinal))
            {
                return group;
            }

            if (legacy is null && string.Equals(group.NickName, MasterGroupBaseName, StringComparison.Ordinal))
            {
                legacy = group;
            }
        }

        return legacy;
    }

    /// <summary>
    /// Records which canvas frame the Conversation Log folded into the prompt, so guardrails
    /// reporting a fresh base checksum (<see cref="CurrentBaseChecksum"/>) stay in the frame the
    /// model is actually reasoning in.
    /// </summary>
    /// <param name="doc">The document the pipeline lives on; null is a no-op.</param>
    /// <param name="harness">The harness holding the pipeline, which is what the frame belongs to.</param>
    /// <param name="groupScoped">True when the folded canvas state was group-scoped.</param>
    internal static void RecordActiveFrame(GH_Document? doc, HarnessComponent? harness, bool groupScoped)
    {
        if (FrameKey(doc, harness) is { } key)
        {
            ActiveFrames.GetOrCreateValue(key).Value = groupScoped;
        }
    }

    /// <summary>
    /// True when the model's canvas view on this document is the group-scoped frame. Defaults to
    /// the full frame until a Conversation Log folds a canvas state.
    /// </summary>
    /// <param name="doc">The document to check; null returns false.</param>
    /// <param name="harness">The harness whose frame is wanted.</param>
    /// <returns>True for the group-scoped frame.</returns>
    internal static bool ActiveFrameIsGroupScoped(GH_Document? doc, HarnessComponent? harness)
        => FrameKey(doc, harness) is { } key
           && ActiveFrames.TryGetValue(key, out StrongBox<bool>? frame)
           && frame.Value;

    /// <summary>
    /// Exports the canvas in the model's active frame and returns the checksum — the single helper
    /// every guardrail uses to hand the model a fresh <c>patch.base.checksum</c>, so the value it
    /// copies always matches the frame its next patch will be verified against.
    /// </summary>
    /// <param name="doc">The document to export; null returns null.</param>
    /// <param name="harness">The harness whose pipeline is reporting, and whose group scopes the frame.</param>
    /// <returns>The checksum, or null when no document is available.</returns>
    internal static string? CurrentBaseChecksum(GH_Document? doc, HarnessComponent? harness)
        => doc is null
            ? null
            : TryExportCanvasState(doc, ActiveFrameIsGroupScoped(doc, harness), harness)?.Checksum;

    /// <summary>
    /// Resolves the reference frame a patch was authored against by MATCHING its carried base
    /// checksum: the active frame's export is tried first, then the other frame's. When neither
    /// matches (real drift) or no checksum was carried, the active frame's snapshot is returned —
    /// that is the frame the model is reading, so the mismatch feedback and its fresh checksum stay
    /// in it. When the master group holds the whole canvas the two frames export identical content
    /// and the choice is immaterial.
    /// </summary>
    /// <param name="doc">The document to export; null falls back to the active canvas.</param>
    /// <param name="harness">The harness whose pipeline authored the patch, and whose group scopes it.</param>
    /// <param name="carriedChecksum">The checksum the patch carried in <c>patch.base.checksum</c>.</param>
    /// <returns>The snapshot of the matching frame, or null when no document is available.</returns>
    internal static CanvasStateSnapshot? ResolveBaseSnapshot(
        GH_Document? doc, HarnessComponent? harness, string? carriedChecksum)
    {
        doc = PhyDocuments.Host(doc) ?? PhyDocuments.ActiveHost();
        if (doc is null)
        {
            return null;
        }

        bool activeFrame = ActiveFrameIsGroupScoped(doc, harness);
        CanvasStateSnapshot? primary = TryExportCanvasState(doc, activeFrame, harness);
        if (primary is null
            || string.IsNullOrEmpty(carriedChecksum)
            || string.Equals(carriedChecksum, primary.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return primary;
        }

        CanvasStateSnapshot? other = TryExportCanvasState(doc, !activeFrame, harness);
        return other is not null
            && string.Equals(carriedChecksum, other.Checksum, StringComparison.OrdinalIgnoreCase)
            ? other
            : primary;
    }

    /// <summary>
    /// Resolves the guids the group-scoped frame may export: the master group's members, expanded
    /// through nested groups, restricted to objects still on the document. Empty when no master
    /// group exists — the scoped grounding then renders nothing and the model starts from a full
    /// document, whose placement creates the group.
    /// </summary>
    /// <param name="doc">The document to resolve against.</param>
    /// <param name="harness">The harness whose group defines the scope.</param>
    /// <returns>The live instanceGuids inside the master group.</returns>
    internal static HashSet<Guid> MasterGroupScope(GH_Document doc, HarnessComponent? harness)
    {
        var scope = new HashSet<Guid>();
        if (FindMasterGroup(doc, harness) is not { } master)
        {
            return scope;
        }

        var queue = new Queue<Guid>(master.ObjectIDs ?? Enumerable.Empty<Guid>());
        while (queue.Count > 0)
        {
            Guid guid = queue.Dequeue();
            if (doc.FindObject(guid, false) is not { } obj || !scope.Add(guid))
            {
                continue;
            }

            if (obj is GHGroupObject nested)
            {
                foreach (Guid member in nested.ObjectIDs ?? Enumerable.Empty<Guid>())
                {
                    queue.Enqueue(member);
                }
            }
        }

        return scope;
    }

    /// <summary>
    /// Enrolls a placement into the master group, creating the group on first use. Model-authored
    /// sub-groups are enrolled as whole members (their contents follow along), and any placed
    /// component not covered by one is enrolled directly — so everything the model places ends up
    /// inside the master group, however it chose to organize itself. Objects already inside
    /// (directly or through nesting) are left alone, so a user's manual re-arrangement inside the
    /// group survives later placements.
    /// </summary>
    /// <param name="doc">The document the placement landed on; null is a no-op.</param>
    /// <param name="harness">The harness whose pipeline placed this, and whose group it belongs in.</param>
    /// <param name="placedComponents">InstanceGuids of the components this placement created.</param>
    /// <param name="modelGroups">
    /// InstanceGuids of the groups this placement created, when the caller knows them (the patch
    /// path does); null infers them as the groups whose members all belong to this placement (the
    /// full-document path, where the library created the groups).
    /// </param>
    internal static void EnrollPlaced(
        GH_Document? doc,
        HarnessComponent? harness,
        IReadOnlyCollection<Guid> placedComponents,
        IReadOnlyCollection<Guid>? modelGroups = null)
    {
        if (doc is null || (placedComponents.Count == 0 && (modelGroups?.Count ?? 0) == 0))
        {
            return;
        }

        GHGroupObject master = EnsureMasterGroup(doc, harness);
        HashSet<Guid> covered = MasterGroupScope(doc, harness);

        var placedSet = new HashSet<Guid>(placedComponents);
        IEnumerable<Guid> groups = modelGroups ?? doc.Objects
            .OfType<GHGroupObject>()
            .Where(g => !IsMasterGroup(g)
                && g.ObjectIDs is { Count: > 0 } members
                && members.All(placedSet.Contains))
            .Select(g => g.InstanceGuid)
            .ToList();

        bool changed = false;
        foreach (Guid groupGuid in groups)
        {
            if (covered.Contains(groupGuid))
            {
                continue;
            }

            master.AddObject(groupGuid);
            changed = true;
            CoverRecursive(doc, groupGuid, covered);
        }

        foreach (Guid guid in placedComponents)
        {
            if (covered.Add(guid))
            {
                master.AddObject(guid);
                changed = true;
            }
        }

        if (changed)
        {
            master.ExpireCaches();
            master.Attributes?.ExpireLayout();
        }
    }

    /// <summary>
    /// Brings the master group into existence BEFORE anything has been placed, with the hint panel
    /// as its only member. Called when the Component Transmitter's arrow is dropped while the
    /// pipeline grounds the model on the group's contents: in that frame the group IS the model's
    /// whole view of the canvas, and until it exists the view is empty, so the gesture that decides
    /// where placements land is also the moment to open the shared workspace and say what it is for.
    /// A group needs at least one member to have bounds at all, which the panel supplies.
    ///
    /// <para>Does nothing when a master group already exists — it then holds whatever the user and
    /// the model have arranged in it, and a second note would be noise. That also means a hint panel
    /// the user deletes stays deleted.</para>
    /// </summary>
    /// <param name="doc">The user's canvas; null is a no-op.</param>
    /// <param name="harness">The harness whose pipeline will place into this group.</param>
    /// <param name="origin">
    /// The placement origin the arrow was dropped on. The panel sits just above it, so the graph the
    /// model places later lands on clear canvas rather than on top of the note.
    /// </param>
    /// <returns>True when the group and its panel were created.</returns>
    internal static bool TryCreateMasterGroupWithHint(
        GH_Document? doc, HarnessComponent? harness, PointF origin)
    {
        if (doc is null || FindMasterGroup(doc, harness) is not null)
        {
            return false;
        }

        var panel = new GHPanel { NickName = HintPanelName };
        panel.CreateAttributes();
        panel.Properties.Colour = HintPanelColour;
        panel.Properties.Multiline = true;
        panel.Properties.Wrap = true;
        panel.SetUserText(HintPanelText);

        var bounds = new RectangleF(
            origin.X,
            origin.Y - HintPanelHeight - HintPanelGap,
            HintPanelWidth,
            HintPanelHeight);
        panel.Attributes.Pivot = new PointF(bounds.X, bounds.Y);
        panel.Attributes.Bounds = bounds;

        doc.AddObject(panel, false);
        panel.Attributes.ExpireLayout();

        GHGroupObject master = EnsureMasterGroup(doc, harness);
        master.AddObject(panel.InstanceGuid);
        master.ExpireCaches();
        master.Attributes?.ExpireLayout();
        doc.DestroyAttributeCache();
        return true;
    }

    // Finds or creates this harness's master group. Creation happens at the first LLM placement (or at
    // the transmitter's drop), so the group materializes exactly where the tip put the components —
    // its bounds are its members', it needs no pivot of its own.
    //
    // A legacy un-suffixed group found here is CLAIMED rather than shared: its nickname gains this
    // harness's id, so it stops answering to every other harness on the canvas. Claiming on the way
    // into a placement, not on a mere read, means an older file still grounds every pipeline on it
    // until one of them actually writes.
    private static GHGroupObject EnsureMasterGroup(GH_Document doc, HarnessComponent? harness)
    {
        string name = MasterGroupName(harness);

        if (FindMasterGroup(doc, harness) is { } existing)
        {
            if (!string.Equals(existing.NickName, name, StringComparison.Ordinal))
            {
                existing.NickName = name;
                existing.Attributes?.ExpireLayout();
            }

            return existing;
        }

        var group = new GHGroupObject
        {
            NickName = name,
            Colour = MasterGroupColour,
        };

        doc.AddObject(group, false);
        if (group.Attributes is not null)
        {
            group.Attributes.Selected = false;
        }

        return group;
    }

    // True for the stem alone (the legacy name) or the stem followed by an id of the right shape.
    // The id check is what keeps a user's own "Physalia tower" group out of this: that is their work,
    // not infrastructure, and it must keep reaching the model.
    private static bool IsMasterGroupName(string? nickname)
    {
        if (string.IsNullOrEmpty(nickname))
        {
            return false;
        }

        if (string.Equals(nickname, MasterGroupBaseName, StringComparison.Ordinal))
        {
            return true;
        }

        if (nickname.Length != MasterGroupBaseName.Length + 1 + MasterGroupTokenLength
            || !nickname.StartsWith(MasterGroupBaseName + " ", StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = MasterGroupBaseName.Length + 1; i < nickname.Length; i++)
        {
            if (!Uri.IsHexDigit(nickname[i]))
            {
                return false;
            }
        }

        return true;
    }

    // What the active-frame flag is filed under: the harness, since each pipeline reads its own frame,
    // falling back to the document for a component that somehow has no harness around it.
    private static object? FrameKey(GH_Document? doc, HarnessComponent? harness)
        => (object?)harness ?? doc;

    // Marks a group and everything nested under it as covered by the master group.
    private static void CoverRecursive(GH_Document doc, Guid groupGuid, HashSet<Guid> covered)
    {
        if (!covered.Add(groupGuid) || doc.FindObject(groupGuid, false) is not GHGroupObject group)
        {
            return;
        }

        foreach (Guid member in group.ObjectIDs ?? Enumerable.Empty<Guid>())
        {
            CoverRecursive(doc, member, covered);
        }
    }
}
