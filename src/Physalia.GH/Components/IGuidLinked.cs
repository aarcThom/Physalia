// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace Physalia.GH.Components;

/// <summary>
/// A component that remembers OTHER document objects by their InstanceGuid, outside the wire graph.
///
/// <para>Grasshopper holds wires as resolved object references and remaps them itself, but a guid kept
/// in a field is opaque to it — so anything that re-issues instance ids has to hand the mapping over.
/// Implement this and <see cref="Harness.DocumentIds.MutateAll"/> will offer it; a component that
/// does not implement it keeps stale links, silently.</para>
///
/// <para><b>Only replace a guid the mapping actually contains.</b> A link may legitimately point
/// OUTSIDE the document being re-issued — PyTransmitter's target script component lives on the user's
/// canvas, not inside the harness — and such a guid must survive untouched.</para>
/// </summary>
internal interface IGuidLinked
{
    /// <summary>
    /// Rewrites this component's stored links through a mapping of old instance id to new.
    /// </summary>
    /// <param name="replacements">
    /// Old id to new id, covering only the objects whose ids actually changed. Guids absent from it
    /// must be left alone.
    /// </param>
    void RemapLinks(IReadOnlyDictionary<Guid, Guid> replacements);
}
