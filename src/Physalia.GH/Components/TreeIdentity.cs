// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.CompilerServices;
using System.Text;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Physalia.GH.Components;

/// <summary>
/// Identifies a data tree, so a component can tell "the same data I saw last solve" from "something
/// upstream recomputed" without comparing the data itself.
///
/// <para>The key is the tree's shape plus the REFERENCE identity of every item on it, which is exact
/// in the one direction that matters. Grasshopper hands back the very goo objects its producer made
/// and only re-makes them when that producer recomputes — so identical references mean nothing
/// upstream ran, and a changed reference means something did. It can therefore report a change for
/// data that recomputed to the same values; it can never miss data that changed. Comparing the values
/// would be the other way round: cheap tests (bounding boxes) silently miss real changes, and honest
/// ones cost a full compare of every Brep on every solve.</para>
/// </summary>
internal static class TreeIdentity
{
    /// <summary>
    /// Builds the identity key for a data tree.
    /// </summary>
    /// <typeparam name="T">The goo type on the tree.</typeparam>
    /// <param name="tree">The tree to key. An empty or null tree keys as the empty string.</param>
    /// <returns>A key that changes when the tree's shape or any item's identity changes.</returns>
    internal static string Of<T>(GH_Structure<T>? tree)
        where T : IGH_Goo
    {
        if (tree is null)
        {
            return string.Empty;
        }

        StringBuilder key = new();

        for (int branch = 0; branch < tree.PathCount; branch++)
        {
            key.Append(tree.Paths[branch]).Append(':');

            foreach (T item in tree.Branches[branch])
            {
                key.Append(item is null ? 0 : RuntimeHelpers.GetHashCode(item)).Append(',');
            }

            key.Append(';');
        }

        return key.ToString();
    }
}
