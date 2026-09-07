// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel;

namespace Physalia.GH.Components;

/// <summary>
/// A small set of named values a pipeline and its model share, per pipeline, for this session.
///
/// <para><b>Keyed on the LOCAL document</b> — the harness a pipeline lives in — so two Pipeline State
/// nodes in one harness see one board and nodes in different harnesses do not. The same pairing
/// device as the spend ledger, the PDF registry and delegation, and it means a state node needs no
/// wire to the tool node that writes it.</para>
///
/// <para><b>Session-only, like the rest of the lifecycle.</b> This is working state — which stage the
/// build is on, which option was chosen, what the last measurement was — and it belongs to the run
/// that produced it. What survives a restart is the conversation (in the project folder) and the
/// model's notes (the Memory tool's files); a stage number from last Tuesday would be a claim about a
/// pipeline that is no longer in that state.</para>
///
/// <para><b>Capped, deliberately.</b> Sixty-four keys and eight kilobytes a value: enough for
/// anything the graph can branch on, and small enough that a model told it can store state cannot
/// quietly start using it as a scratch disk — which the Memory tool's files are actually for.</para>
/// </summary>
internal static class StateStore
{
    /// <summary>The most keys one pipeline may hold.</summary>
    internal const int MaxKeys = 64;

    /// <summary>The longest a single value may be.</summary>
    internal const int MaxValueLength = 8 * 1024;

    private static readonly ConditionalWeakTable<GH_Document, Board> Boards = new();

    private static readonly object Gate = new();

    /// <summary>
    /// Sets a value.
    /// </summary>
    /// <param name="document">The pipeline's document.</param>
    /// <param name="key">The key; trimmed, and compared without regard to case.</param>
    /// <param name="value">The value; truncated at <see cref="MaxValueLength"/>.</param>
    /// <returns>Null on success, or why it was refused.</returns>
    internal static string? Set(GH_Document? document, string key, string value)
    {
        if (document is null)
        {
            return "This node is not on a document, so there is nowhere to keep state.";
        }

        string name = (key ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return "A key is required.";
        }

        string body = value ?? string.Empty;
        if (body.Length > MaxValueLength)
        {
            body = body.Substring(0, MaxValueLength);
        }

        lock (Gate)
        {
            Board board = Boards.GetValue(document, _ => new Board());

            if (!board.Values.ContainsKey(name) && board.Values.Count >= MaxKeys)
            {
                // Refused rather than silently evicting something: the graph may be gating on a key
                // this would have dropped, and a gate that quietly reopens is worse than a refusal
                // the model can read and act on.
                return $"This pipeline already holds {MaxKeys} keys, which is the limit. Clear one before setting another.";
            }

            board.Values[name] = body;

            // Removed WITHOUT regard to case, because Values is keyed that way and List.Remove is
            // not: setting "stage" and then "STAGE" left one board entry but two order entries, and
            // All() — which filters the order log through the case-insensitive Values — then handed
            // the outputs the same value twice under two spellings. A duplicate on Keys/Values
            // shifts anything downstream matching by index, which is most of what this node is for.
            board.Order.RemoveAll(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            board.Order.Add(name);
        }

        return null;
    }

    /// <summary>
    /// Reads a value.
    /// </summary>
    /// <param name="document">The pipeline's document.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or null when the key is not set.</returns>
    internal static string? Get(GH_Document? document, string? key)
    {
        if (document is null || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        lock (Gate)
        {
            return Boards.TryGetValue(document, out Board? board)
                && board.Values.TryGetValue(key.Trim(), out string? value)
                    ? value
                    : null;
        }
    }

    /// <summary>
    /// Every key and value, in the order the keys were last set.
    ///
    /// <para>Last-set order rather than alphabetical, because that is the order that carries
    /// information: what a pipeline touched most recently is what it is working on.</para>
    /// </summary>
    /// <param name="document">The pipeline's document.</param>
    /// <returns>The pairs.</returns>
    internal static IReadOnlyList<KeyValuePair<string, string>> All(GH_Document? document)
    {
        if (document is null)
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }

        lock (Gate)
        {
            if (!Boards.TryGetValue(document, out Board? board))
            {
                return Array.Empty<KeyValuePair<string, string>>();
            }

            return board.Order
                .Where(k => board.Values.ContainsKey(k))
                .Select(k => new KeyValuePair<string, string>(k, board.Values[k]))
                .ToList();
        }
    }

    /// <summary>
    /// Removes one key, or all of them.
    /// </summary>
    /// <param name="document">The pipeline's document.</param>
    /// <param name="key">The key to remove, or null/blank to remove everything.</param>
    /// <returns>How many keys were removed.</returns>
    internal static int Clear(GH_Document? document, string? key)
    {
        if (document is null)
        {
            return 0;
        }

        lock (Gate)
        {
            if (!Boards.TryGetValue(document, out Board? board))
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                int count = board.Values.Count;
                board.Values.Clear();
                board.Order.Clear();
                return count;
            }

            string name = key.Trim();

            // Case-insensitively, for the same reason as Set: a case-variant spelling would
            // otherwise remove the value and leave its name in the order log. All() filters that
            // out, so nothing shows — which is exactly why it would accumulate unnoticed.
            board.Order.RemoveAll(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            return board.Values.Remove(name) ? 1 : 0;
        }
    }

    private sealed class Board
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal List<string> Order { get; } = new();
    }
}
