// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Triggers;

/// <summary>
/// What happened to one file under a watched folder.
/// </summary>
public enum FolderChangeKind
{
    /// <summary>The file appeared.</summary>
    Added,

    /// <summary>The file's contents or timestamp changed.</summary>
    Changed,

    /// <summary>The file went away.</summary>
    Removed,

    /// <summary>The file was renamed; <see cref="FolderChange.RelativePath"/> is where it ended up.</summary>
    Renamed,
}

/// <summary>
/// One change under a watched folder, as the pipeline is told about it.
///
/// <para>The path is kept RELATIVE to the watched root, because that is what the payload should say:
/// an absolute path is mostly the same forty characters on every line, and the folder is already
/// named once in the summary's first line. Callers that need the absolute path recombine it with the
/// root they were watching.</para>
/// </summary>
/// <param name="RelativePath">Where the file sits under the watched root, using forward slashes.</param>
/// <param name="Kind">What happened to it.</param>
public sealed record FolderChange(string RelativePath, FolderChangeKind Kind);
