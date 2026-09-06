// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;

namespace Physalia.Core.Packaging;

/// <summary>
/// The Physalia half of a <c>.phy</c> package: everything about a harness that is not in its
/// Grasshopper document.
///
/// <para>It exists because the harness's own metadata has nowhere else to live. A preset is the
/// archive of the harness's SUB-document, and the harness component itself is not in there — so once
/// the notes stopped being a component sitting inside the pipeline, a plain <c>.gh</c> had no room
/// for them. That is the whole reason the package format exists, and why deleting the Harness Notes
/// component and inventing <c>.phy</c> were one change rather than two.</para>
///
/// <para><b>This is the one place in Physalia where a version field earns its keep.</b> The MCP and
/// API stores deliberately carry none — they are configuration this machine writes and reads, and a
/// shape that changes is a shape we changed. A package is different: it is written by one person's
/// Physalia and read by another's, months later, across a firm. It will be read by a version that
/// did not write it.</para>
/// </summary>
/// <param name="FormatVersion">
/// The package layout this manifest describes. A reader refuses a version from the future rather
/// than guessing at it.
/// </param>
/// <param name="Name">
/// The harness's name — its nickname, and through it the name of its project-files folder. Restored
/// on import, so a workflow arrives called what its author called it.
/// </param>
/// <param name="Description">
/// What this pipeline is for, shown in the chat window's preset gallery. Replaces what the Harness
/// Notes component used to hold.
/// </param>
/// <param name="ChatText">
/// What the chat window says in place of its usual invitation to start typing, so a shared pipeline
/// can open with its own instructions.
/// </param>
/// <param name="CreatedUtc">When the package was written.</param>
/// <param name="Downloads">
/// What the pipeline fetched rather than what it carries — url, file name and size for each file the
/// download tool brought in. A 400MB LiDAR tile is re-fetchable and is left out of the package; the
/// knowledge of WHICH tile is two hundred bytes and is the part worth shipping.
/// </param>
public sealed record PhyManifest(
    int FormatVersion,
    string Name,
    string? Description,
    string? ChatText,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<PhyDownloadRecord> Downloads)
{
    /// <summary>
    /// The package layout this build writes.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Creates a manifest for a package being written now.
    /// </summary>
    /// <param name="name">The harness's name.</param>
    /// <param name="description">The gallery description, or null.</param>
    /// <param name="chatText">The chat window's opening text, or null.</param>
    /// <param name="downloads">The download ledger, or null for none.</param>
    /// <returns>The manifest.</returns>
    public static PhyManifest For(
        string name,
        string? description,
        string? chatText,
        IReadOnlyList<PhyDownloadRecord>? downloads = null) =>
        new(
            CurrentFormatVersion,
            name,
            Blank(description),
            Blank(chatText),
            DateTimeOffset.UtcNow,
            downloads ?? Array.Empty<PhyDownloadRecord>());

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// One file the pipeline fetched, recorded so the other end can fetch it again instead of receiving
/// a copy.
/// </summary>
/// <param name="Url">Where it came from.</param>
/// <param name="File">The file name it was saved under, relative to the project folder.</param>
/// <param name="Bytes">How big it was, so the other end knows what it is agreeing to.</param>
public sealed record PhyDownloadRecord(string Url, string File, long Bytes);
