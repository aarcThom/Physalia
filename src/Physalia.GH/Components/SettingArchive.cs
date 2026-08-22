// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using GH_IO.Serialization;

namespace Physalia.GH.Components;

/// <summary>
/// Reads and writes the optional settings a component holds on the chat window's behalf — which
/// clusters the model may use, which catalog panels are folded in, which unit text is handed over,
/// what text rides alongside a snapshot.
///
/// <para>Every one of those settings is <b>nullable on purpose</b>, and the null-vs-empty distinction
/// is load-bearing: null means "not configured" (include everything, use the document's own value)
/// while an empty selection means "include nothing". Grasshopper's archive has no null, so each value
/// is stored as a <c>&lt;key&gt;Set</c> boolean plus the value itself — and since that discipline is
/// easy to half-apply, it is stated once here rather than open-coded in every component that owns a
/// setting.</para>
/// </summary>
internal static class SettingArchive
{
    /// <summary>
    /// Writes an optional string: the set flag, and the value when there is one.
    /// </summary>
    /// <param name="writer">The archive writer.</param>
    /// <param name="key">The base key; the flag is written as "&lt;key&gt;Set".</param>
    /// <param name="value">The value, or null when not configured.</param>
    internal static void WriteOptionalString(GH_IWriter writer, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.SetBoolean(key + "Set", value is not null);
        if (value is not null)
        {
            writer.SetString(key, value);
        }
    }

    /// <summary>
    /// Reads an optional string written by <see cref="WriteOptionalString"/>.
    /// </summary>
    /// <param name="reader">The archive reader.</param>
    /// <param name="key">The base key.</param>
    /// <returns>The stored value, or null when it was not configured (or the key is absent).</returns>
    internal static string? ReadOptionalString(GH_IReader reader, string key)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (!reader.ItemExists(key + "Set") || !reader.GetBoolean(key + "Set"))
        {
            return null;
        }

        return reader.ItemExists(key) ? reader.GetString(key) : null;
    }

    /// <summary>
    /// Writes an optional list of names: the set flag, the count, and one indexed item per name.
    /// </summary>
    /// <param name="writer">The archive writer.</param>
    /// <param name="key">The base key; the flag, count and items derive from it.</param>
    /// <param name="names">The names, or null when not configured.</param>
    internal static void WriteOptionalNames(GH_IWriter writer, string key, IReadOnlyList<string>? names)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.SetBoolean(key + "Set", names is not null);
        if (names is null)
        {
            return;
        }

        writer.SetInt32(key + "Count", names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            writer.SetString(key + "Item", i, names[i]);
        }
    }

    /// <summary>
    /// Reads an optional list of names written by <see cref="WriteOptionalNames"/>.
    /// </summary>
    /// <param name="reader">The archive reader.</param>
    /// <param name="key">The base key.</param>
    /// <returns>The stored names (possibly empty, meaning "include nothing"), or null when not configured.</returns>
    internal static IReadOnlyList<string>? ReadOptionalNames(GH_IReader reader, string key)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (!reader.ItemExists(key + "Set") || !reader.GetBoolean(key + "Set"))
        {
            return null;
        }

        int count = reader.ItemExists(key + "Count") ? reader.GetInt32(key + "Count") : 0;
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            names.Add(reader.GetString(key + "Item", i));
        }

        return names;
    }

    /// <summary>
    /// Writes an optional list of category/sub-category pairs — the two-level catalog selection.
    /// </summary>
    /// <param name="writer">The archive writer.</param>
    /// <param name="key">The base key; the flag, count and both halves of each pair derive from it.</param>
    /// <param name="leaves">The selected leaves, or null when not configured.</param>
    internal static void WriteOptionalLeaves(
        GH_IWriter writer,
        string key,
        IReadOnlyList<(string Category, string SubCategory)>? leaves)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.SetBoolean(key + "Set", leaves is not null);
        if (leaves is null)
        {
            return;
        }

        writer.SetInt32(key + "Count", leaves.Count);
        for (int i = 0; i < leaves.Count; i++)
        {
            writer.SetString(key + "Category", i, leaves[i].Category);
            writer.SetString(key + "SubCategory", i, leaves[i].SubCategory);
        }
    }

    /// <summary>
    /// Reads an optional list of category/sub-category pairs written by <see cref="WriteOptionalLeaves"/>.
    /// </summary>
    /// <param name="reader">The archive reader.</param>
    /// <param name="key">The base key.</param>
    /// <returns>The stored leaves (possibly empty, meaning "include nothing"), or null when not configured.</returns>
    internal static IReadOnlyList<(string Category, string SubCategory)>? ReadOptionalLeaves(
        GH_IReader reader,
        string key)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (!reader.ItemExists(key + "Set") || !reader.GetBoolean(key + "Set"))
        {
            return null;
        }

        int count = reader.ItemExists(key + "Count") ? reader.GetInt32(key + "Count") : 0;
        var leaves = new List<(string, string)>(count);
        for (int i = 0; i < count; i++)
        {
            leaves.Add((reader.GetString(key + "Category", i), reader.GetString(key + "SubCategory", i)));
        }

        return leaves;
    }
}
