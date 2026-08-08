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

namespace Physalia.GH.Generation;

/// <summary>
/// Master-group half of the façade: every component the model places is enrolled into one canvas
/// group named "Physalia" — the shared workspace between the model and the user. The group comes
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
    /// Nickname identifying the master group on the canvas. A user renaming the group detaches it
    /// from Physalia (a fresh one is created on the next placement); naming their own group this
    /// way adopts it — both are deliberate, the name IS the contract.
    /// </summary>
    internal const string MasterGroupName = "Physalia";

    // Faint translucent teal, distinct from Grasshopper's default group colour so the shared
    // workspace reads as Physalia's at a glance without shouting over the model's own group colours.
    private static readonly Color MasterGroupColour = Color.FromArgb(45, 105, 155, 170);

    // Which reference frame the Conversation Log last folded for each document: true = the model is
    // reading the group-scoped canvas state. Session-only, weak-keyed, like the stable-id registry.
    private static readonly ConditionalWeakTable<GH_Document, StrongBox<bool>> ActiveFrames = new();

    /// <summary>
    /// True when the object is the master group. Used by exports and layout to treat it as
    /// invisible infrastructure rather than a model-authored functional area.
    /// </summary>
    /// <param name="obj">The document object to test.</param>
    /// <returns>True for a group nicknamed <see cref="MasterGroupName"/>.</returns>
    internal static bool IsMasterGroup(IGH_DocumentObject? obj)
        => obj is GHGroupObject group
           && string.Equals(group.NickName, MasterGroupName, StringComparison.Ordinal);

    /// <summary>
    /// Finds the master group on the document, or null when none exists yet.
    /// </summary>
    /// <param name="doc">The document to search; null returns null.</param>
    /// <returns>The master group, or null.</returns>
    internal static GHGroupObject? FindMasterGroup(GH_Document? doc)
        => doc?.Objects.OfType<GHGroupObject>().FirstOrDefault(IsMasterGroup);

    /// <summary>
    /// Records which canvas frame the Conversation Log folded into the prompt, so guardrails
    /// reporting a fresh base checksum (<see cref="CurrentBaseChecksum"/>) stay in the frame the
    /// model is actually reasoning in.
    /// </summary>
    /// <param name="doc">The document the pipeline lives on; null is a no-op.</param>
    /// <param name="groupScoped">True when the folded canvas state was group-scoped.</param>
    internal static void RecordActiveFrame(GH_Document? doc, bool groupScoped)
    {
        if (doc is not null)
        {
            ActiveFrames.GetOrCreateValue(doc).Value = groupScoped;
        }
    }

    /// <summary>
    /// True when the model's canvas view on this document is the group-scoped frame. Defaults to
    /// the full frame until a Conversation Log folds a canvas state.
    /// </summary>
    /// <param name="doc">The document to check; null returns false.</param>
    /// <returns>True for the group-scoped frame.</returns>
    internal static bool ActiveFrameIsGroupScoped(GH_Document? doc)
        => doc is not null
           && ActiveFrames.TryGetValue(doc, out StrongBox<bool>? frame)
           && frame.Value;

    /// <summary>
    /// Exports the canvas in the model's active frame and returns the checksum — the single helper
    /// every guardrail uses to hand the model a fresh <c>patch.base.checksum</c>, so the value it
    /// copies always matches the frame its next patch will be verified against.
    /// </summary>
    /// <param name="doc">The document to export; null returns null.</param>
    /// <returns>The checksum, or null when no document is available.</returns>
    internal static string? CurrentBaseChecksum(GH_Document? doc)
        => doc is null ? null : TryExportCanvasState(doc, ActiveFrameIsGroupScoped(doc))?.Checksum;

    /// <summary>
    /// Resolves the reference frame a patch was authored against by MATCHING its carried base
    /// checksum: the active frame's export is tried first, then the other frame's. When neither
    /// matches (real drift) or no checksum was carried, the active frame's snapshot is returned —
    /// that is the frame the model is reading, so the mismatch feedback and its fresh checksum stay
    /// in it. When the master group holds the whole canvas the two frames export identical content
    /// and the choice is immaterial.
    /// </summary>
    /// <param name="doc">The document to export; null falls back to the active canvas.</param>
    /// <param name="carriedChecksum">The checksum the patch carried in <c>patch.base.checksum</c>.</param>
    /// <returns>The snapshot of the matching frame, or null when no document is available.</returns>
    internal static CanvasStateSnapshot? ResolveBaseSnapshot(GH_Document? doc, string? carriedChecksum)
    {
        doc = PhyDocuments.Host(doc) ?? PhyDocuments.ActiveHost();
        if (doc is null)
        {
            return null;
        }

        bool activeFrame = ActiveFrameIsGroupScoped(doc);
        CanvasStateSnapshot? primary = TryExportCanvasState(doc, activeFrame);
        if (primary is null
            || string.IsNullOrEmpty(carriedChecksum)
            || string.Equals(carriedChecksum, primary.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return primary;
        }

        CanvasStateSnapshot? other = TryExportCanvasState(doc, !activeFrame);
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
    /// <returns>The live instanceGuids inside the master group.</returns>
    internal static HashSet<Guid> MasterGroupScope(GH_Document doc)
    {
        var scope = new HashSet<Guid>();
        if (FindMasterGroup(doc) is not { } master)
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
    /// <param name="placedComponents">InstanceGuids of the components this placement created.</param>
    /// <param name="modelGroups">
    /// InstanceGuids of the groups this placement created, when the caller knows them (the patch
    /// path does); null infers them as the groups whose members all belong to this placement (the
    /// full-document path, where the library created the groups).
    /// </param>
    internal static void EnrollPlaced(
        GH_Document? doc,
        IReadOnlyCollection<Guid> placedComponents,
        IReadOnlyCollection<Guid>? modelGroups = null)
    {
        if (doc is null || (placedComponents.Count == 0 && (modelGroups?.Count ?? 0) == 0))
        {
            return;
        }

        GHGroupObject master = EnsureMasterGroup(doc);
        HashSet<Guid> covered = MasterGroupScope(doc);

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

    // Finds or creates the master group. Creation happens at the first LLM placement, so the group
    // materializes exactly where the transmitter's tip put the components — its bounds are its
    // members', it needs no pivot of its own.
    private static GHGroupObject EnsureMasterGroup(GH_Document doc)
    {
        if (FindMasterGroup(doc) is { } existing)
        {
            return existing;
        }

        var group = new GHGroupObject
        {
            NickName = MasterGroupName,
            Colour = MasterGroupColour,
        };

        doc.AddObject(group, false);
        if (group.Attributes is not null)
        {
            group.Attributes.Selected = false;
        }

        return group;
    }

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
