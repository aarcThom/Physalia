// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace Physalia.GH.Parameters;

/// <summary>
/// A generic parameter whose nickname is shared with a parameter somewhere else, and which reports an
/// edit so the other end can follow.
///
/// <para>Used for the two ends of a harness port. A Harness In's output and the input it grows on
/// the proxy are ONE name, editable from either side. A Harness Out's input names the grip the proxy
/// paints for it, which has no editor of its own, so that pair travels outward only.</para>
///
/// <para>Overriding the property is the only way to see that happen. Grasshopper's own
/// <c>NickName</c> setter (declared on <c>GH_InstanceDescription</c>) raises no event at all — its
/// body is a bare field assignment — and only the right-click name box announces a rename, so an F2
/// or properties-panel rename reaches no handler anywhere. Reconciling at layout instead does not
/// work either: <c>PerformLayout</c> is called from a bare handful of places and the paint loop is
/// not one of them, so layout happens on solution and an expired one can go unperformed. The setter
/// IS virtual, which makes overriding it the one hook that cannot be missed.</para>
///
/// <para>The recursion the two ends would otherwise make is cut by the equality guard: the far end
/// pushes back the very name that arrived, so the return trip finds nothing to change.</para>
/// </summary>
public abstract class Param_LinkedName : Param_GenericObject
{
    /// <summary>
    /// Gets or sets what to do when this parameter is renamed: carry the new name to the other end.
    /// Null before the owner has bound it, and while an archive is being read — where the name
    /// arriving is the one the pair was saved with, so there is nothing to carry.
    /// </summary>
    internal Action<string>? Renamed { get; set; }

    /// <inheritdoc/>
    public override string NickName
    {
        get => base.NickName;

        set
        {
            if (string.Equals(base.NickName, value, StringComparison.Ordinal))
            {
                return;
            }

            base.NickName = value;
            Renamed?.Invoke(value);
        }
    }
}
