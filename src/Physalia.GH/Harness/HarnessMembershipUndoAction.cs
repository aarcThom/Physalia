// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Physalia.GH.Components;

namespace Physalia.GH.Harness;

/// <summary>
/// Undo/redo action for an "add to harness" or "remove from harness" edit. Records the exact
/// set of members the edit changed and reverses it through the same <see cref="Harness"/> calls
/// the user action used, so hidden members are restored (or re-hidden) consistently. Reversal
/// is by membership delta, not a full state snapshot, so undoing an add removes only the members
/// that add introduced — not any pre-existing ones.
/// </summary>
internal sealed class HarnessMembershipUndoAction : GH_UndoAction
{
    private readonly Guid _chatboxId;
    private readonly List<Guid> _members;
    private readonly bool _added;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessMembershipUndoAction"/> class.
    /// </summary>
    /// <param name="chatboxId">The InstanceGuid of the Chatbox whose group changed.</param>
    /// <param name="members">The members the edit actually added or removed.</param>
    /// <param name="added">true if the edit added these members; false if it removed them.</param>
    public HarnessMembershipUndoAction(Guid chatboxId, IEnumerable<Guid> members, bool added)
    {
        _chatboxId = chatboxId;
        _members = members.ToList();
        _added = added;
    }

    /// <inheritdoc/>
    protected override void Internal_Undo(GH_Document doc) => Reverse(doc, undo: true);

    /// <inheritdoc/>
    protected override void Internal_Redo(GH_Document doc) => Reverse(doc, undo: false);

    // Undo of an add removes; redo of an add re-adds; remove is the mirror.
    private void Reverse(GH_Document doc, bool undo)
    {
        if (doc?.FindObject(_chatboxId, false) is not Chatbox chatbox)
        {
            return;
        }

        bool add = _added ? !undo : undo;
        if (add)
        {
            chatbox.Group.Add(_members);
        }
        else
        {
            chatbox.Group.Remove(_members);
        }
    }
}
