// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// Identifies the component an event came from: its instance guid plus the display name it had
/// when the event was minted. Provenance only — nothing routes, gates, or renders data by it; it
/// exists so a human reading the trace or the chat window can tell WHICH node produced a turn.
///
/// <para>The name is a snapshot for the case where the component is gone (deleted, or the guid
/// belongs to another document): a live lookup by <see cref="Id"/> gives the current nickname and
/// icon, and this is the fallback when that lookup finds nothing.</para>
/// </summary>
/// <param name="Id">Instance guid of the component that produced the event.</param>
/// <param name="Name">Display name of that component at mint time, used when it can no longer be found.</param>
public sealed record ComponentOrigin(Guid Id, string Name);
