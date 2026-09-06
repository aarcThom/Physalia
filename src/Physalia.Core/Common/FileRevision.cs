// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// A cheap stamp for "has this file changed since I last read it".
/// </summary>
/// <remarks>
/// <para>Used by the per-user config stores and by everything that caches a read of one — the chat
/// window's setup pages, and the nodes that hold a copy of the list. A <c>FileInfo</c> stat is
/// microseconds, which is what makes it affordable on a Grasshopper solve and on the chat window's
/// 0.15 s tick, where re-parsing the file would not be.</para>
/// <para><b>Write time AND length</b>, because a file system's write-time resolution is coarse
/// enough that a small edit saved twice in quick succession can land on the same tick; the length
/// catches most of what the timestamp misses. This is a cache hint, not an integrity check.</para>
/// <para>A missing file and an unreadable one get their own stamps rather than sharing one: "absent"
/// is a normal state that a later save changes, and collapsing the two would make the first save
/// after a transient IO error look like no change at all.</para>
/// </remarks>
public static class FileRevision
{
    /// <summary>
    /// Returns a stamp that changes whenever the file at the given path does.
    /// </summary>
    /// <param name="path">Absolute path of the file to stamp.</param>
    /// <returns>An opaque stamp; compare with a previously held one, never parse it.</returns>
    public static string Stamp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "none";

        try
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{info.LastWriteTimeUtc.Ticks}|{info.Length}" : "none";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }
}
