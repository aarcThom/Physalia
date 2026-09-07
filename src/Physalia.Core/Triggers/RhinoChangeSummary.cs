// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text;

namespace Physalia.Core.Triggers;

/// <summary>
/// The kinds of Rhino-document change a trigger can be armed for. Flags, because one coalesced
/// burst routinely spans more than one — drawing a curve on a new layer is geometry AND layers, and
/// a person who asked to hear about geometry should not have that reported as two separate wakes.
/// </summary>
[Flags]
public enum RhinoChangeKind
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Objects added, deleted, replaced, transformed, or their attributes edited.</summary>
    Geometry = 1,

    /// <summary>What the user has selected.</summary>
    Selection = 2,

    /// <summary>The layer table.</summary>
    Layers = 4,

    /// <summary>A different file: new, opened, or the active document swapped.</summary>
    Document = 8,

    /// <summary>Everything.</summary>
    All = Geometry | Selection | Layers | Document,
}

/// <summary>
/// Describes what changed in the Rhino document, as the payload a trigger's signal carries.
///
/// <para>Deliberately does NOT describe the document's contents: that is the Rhino Document
/// grounder's job, it says it in one voice already, and it is recomputed before the prompt is
/// assembled anyway. What this says is what a <em>wake-up</em> has to say and grounding cannot — the
/// fact that something changed, and roughly what — because by the time the model reads the prompt
/// the change is indistinguishable from the state it was always in.</para>
/// </summary>
public static class RhinoChangeSummary
{
    /// <summary>
    /// Describes a coalesced burst of Rhino document events.
    /// </summary>
    /// <param name="kinds">Which kinds of change the burst covered.</param>
    /// <param name="objectCount">How many objects the document holds now.</param>
    /// <param name="selectedCount">How many of them are selected now.</param>
    /// <returns>The payload text; a generic line when the kinds are empty, so a signal is never blank.</returns>
    public static string Describe(RhinoChangeKind kinds, int objectCount, int selectedCount)
    {
        var parts = new List<string>(4);

        if (kinds.HasFlag(RhinoChangeKind.Document))
        {
            parts.Add("a different file is open");
        }

        if (kinds.HasFlag(RhinoChangeKind.Geometry))
        {
            parts.Add("geometry was edited");
        }

        if (kinds.HasFlag(RhinoChangeKind.Layers))
        {
            parts.Add("the layer table changed");
        }

        if (kinds.HasFlag(RhinoChangeKind.Selection))
        {
            parts.Add("the selection changed");
        }

        StringBuilder text = new("The Rhino document changed");

        if (parts.Count > 0)
        {
            text.Append(": ").Append(Join(parts));
        }

        text.Append('.');

        // The counts go on the same payload because a selection wake is only actionable with them:
        // "the selection changed" and "3 objects are selected" are one fact split in two, and the
        // second half is what makes a phrase like "move these" resolvable in the turn it arrives in.
        text.Append(' ')
            .Append(objectCount.ToString(CultureInfo.InvariantCulture))
            .Append(objectCount == 1 ? " object in the document, " : " objects in the document, ")
            .Append(selectedCount.ToString(CultureInfo.InvariantCulture))
            .Append(" selected.");

        return text.ToString();
    }

    private static string Join(List<string> parts) =>
        parts.Count switch
        {
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
}
