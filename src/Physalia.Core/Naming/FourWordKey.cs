// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Naming;

/// <summary>
/// Turns an instance id into a memorable four-word name — <c>curious-cake-soap-fun</c> — used as a
/// harness's default nickname and, through it, as the name of its project-files folder.
///
/// <para><b>Derived, never randomised.</b> The name is a pure function of the id, which is what makes
/// it free: nothing has to be generated, serialized or kept in step. It is stable across save and
/// load because the id is; a copy-pasted harness is renamed automatically because Grasshopper issues
/// the copy a new id; and a preset placed twice yields two different names because
/// <c>DocumentIds.MutateAll</c> re-issues every id on load. That last one matters most — two
/// harnesses sharing a name would share a project folder and quietly overwrite each other's
/// downloads. The master group's name is built from the same id for the same reason.</para>
///
/// <para>The words are all lower-case a-z, so the name survives folder-name sanitizing untouched and
/// what is written on the canvas is exactly what is written on disk. They are short, unambiguous read
/// aloud, and deliberately share nothing with Grasshopper's own vocabulary, so no name can be
/// mistaken for a description of what the harness does.</para>
/// </summary>
public static class FourWordKey
{
    /// <summary>
    /// The separator between words. Also legal in a folder name, which is the point.
    /// </summary>
    public const char Separator = '-';

    /// <summary>
    /// How many words a generated name has.
    /// </summary>
    public const int WordCount = 4;

    // 256 words, so each word consumes exactly one byte of the id and the whole name costs four —
    // 2^32 names, which is far past what a document (or a firm) will ever hold. Sized as a power of
    // two on purpose: a byte indexes it directly, with no modulo bias to reason about.
    private static readonly string[] WordList =
    {
        "cake", "jam", "plum", "pear", "bean", "corn", "rice", "soup",
        "milk", "bun", "pie", "fig", "oat", "sage", "mint", "honey",
        "lime", "kale", "leek", "date", "yam", "cocoa", "peach", "berry",
        "mango", "waffle", "syrup", "toast", "pepper", "basil", "clove", "ginger",
        "otter", "robin", "finch", "heron", "moose", "bison", "hare", "lynx",
        "seal", "crab", "moth", "wren", "dove", "koala", "gecko", "tapir",
        "ibis", "lark", "mole", "newt", "owl", "ram", "swan", "toad",
        "vole", "wasp", "yak", "zebra", "panda", "sloth", "badger", "puffin",
        "river", "brook", "cliff", "dune", "fern", "grove", "marsh", "meadow",
        "ridge", "shore", "tundra", "valley", "willow", "birch", "cedar", "maple",
        "aspen", "alder", "holly", "ivy", "moss", "reed", "thorn", "tulip",
        "daisy", "lotus", "poppy", "clover", "acorn", "pebble", "boulder", "canyon",
        "cloud", "storm", "breeze", "frost", "hail", "mist", "rain", "snow",
        "thunder", "sunny", "dawn", "dusk", "noon", "star", "comet", "moon",
        "aurora", "zenith", "gale", "drizzle", "monsoon", "rainbow", "eclipse", "meteor",
        "nebula", "orbit", "solar", "lunar", "tide", "ember", "flame", "spark",
        "anvil", "basket", "bottle", "bridge", "bucket", "candle", "cottage", "drum",
        "engine", "fabric", "hammer", "kettle", "ladder", "lantern", "mirror", "needle",
        "pocket", "quilt", "ribbon", "saddle", "teapot", "thimble", "vase", "violin",
        "whistle", "window", "anchor", "beacon", "compass", "harbor", "clock", "lens",
        "curious", "gentle", "bright", "quiet", "swift", "brave", "clever", "humble",
        "jolly", "keen", "lively", "merry", "noble", "patient", "quirky", "rapid",
        "serene", "tender", "upbeat", "vivid", "warm", "witty", "zesty", "ample",
        "bold", "calm", "dapper", "eager", "fancy", "glad", "happy", "ideal",
        "amber", "azure", "coral", "ivory", "jade", "ochre", "scarlet", "teal",
        "violet", "crimson", "golden", "silver", "copper", "bronze", "pearl", "ruby",
        "opal", "topaz", "indigo", "lilac", "mauve", "russet", "sable", "umber",
        "beige", "cobalt", "emerald", "garnet", "hazel", "khaki", "magenta", "saffron",
        "amble", "bounce", "chuckle", "dazzle", "drift", "echo", "flicker", "glide",
        "hum", "jaunt", "kindle", "linger", "mingle", "nestle", "ponder", "ramble",
        "shimmer", "tumble", "unwind", "wander", "whisper", "yonder", "zephyr", "bramble",
        "cascade", "dapple", "festoon", "glimmer", "hollow", "jubilee", "lullaby", "marvel",
    };

    private static readonly HashSet<string> WordSet = new(WordList, StringComparer.Ordinal);

    /// <summary>
    /// Gets the words a generated name is built from, in index order.
    /// </summary>
    public static IReadOnlyList<string> Words => WordList;

    /// <summary>
    /// Builds the four-word name for an instance id.
    /// </summary>
    /// <param name="id">The id to name — a component's InstanceGuid.</param>
    /// <returns>Four lower-case words joined by hyphens.</returns>
    public static string From(Guid id)
    {
        // The first four bytes are Guid's Data1 field, which is fully random in a version-4 guid —
        // unlike bytes 7 and 8, which carry the version and variant bits and would waste entropy.
        byte[] bytes = id.ToByteArray();
        return string.Join(
            Separator,
            Enumerable.Range(0, WordCount).Select(i => WordList[bytes[i]]));
    }

    /// <summary>
    /// Determines whether a name has the shape this class generates: exactly four hyphen-separated
    /// words, every one of them from the list.
    ///
    /// <para>This is how a caller tells an auto-assigned name from one a person chose, WITHOUT
    /// comparing against a freshly derived name — which would be wrong exactly when it matters, since
    /// a pasted harness carries the name of the id it was copied from and no longer matches its own.
    /// A person who types four of these words in a row loses their name to a re-derivation; that is
    /// one chance in four billion and costs them a rename.</para>
    /// </summary>
    /// <param name="name">The name to test; null and blank are not this shape.</param>
    /// <returns>True when the name looks auto-assigned.</returns>
    public static bool IsGeneratedShape(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string[] parts = name.Split(Separator);
        return parts.Length == WordCount && parts.All(WordSet.Contains);
    }
}
