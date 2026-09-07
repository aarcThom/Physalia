// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using Grasshopper.Kernel;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves the document a component may schedule a solution on, when the thing asking for that
/// solution came from OUTSIDE Grasshopper — a clock, a file-system watcher, a Rhino edit, another
/// harness handing over a task.
///
/// <para><b>The trap this exists to close.</b> Grasshopper drops scheduled solutions on a disabled
/// document. A harness sub-document's <c>Enabled</c> flag is Physalia's own invariant rather than a
/// user setting — the proxy re-asserts it on every solve — but the proxy only solves when the host
/// document does, and the whole point of an external wake-up is that it arrives when nothing has
/// solved for a while. Without this, such a wake-up is silently dropped: no error, no signal, and a
/// trigger that looks armed and does nothing.</para>
///
/// <para><b>Only a harness document is ever re-enabled.</b> On the user's own file that same flag is
/// Grasshopper's solver lock, and switching it back on would restart a solver somebody switched off
/// on purpose — which is a far worse failure than a dropped wake-up.</para>
/// </summary>
internal static class PipelineWake
{
    /// <summary>
    /// The component's own document, made ready to receive a scheduled solution.
    /// </summary>
    /// <param name="component">The component about to schedule.</param>
    /// <returns>
    /// The document to schedule on, or null when the component is not on one — in which case there is
    /// nothing to wake and the caller should do nothing.
    /// </returns>
    internal static GH_Document? Ready(IGH_DocumentObject? component)
    {
        GH_Document? document = component?.OnPingDocument();
        if (document is null)
        {
            return null;
        }

        if (!document.Enabled && PhyDocuments.IsHarnessDocument(document))
        {
            document.Enabled = true;
        }

        return document;
    }
}
