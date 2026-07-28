// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// How a system-prompt segment behaves across the turns of one session — the only thing that
/// decides whether it can sit inside a provider's cached prefix.
/// </summary>
public enum SystemPromptStability
{
    /// <summary>
    /// Byte-identical for the life of the session: the preamble, the schema, the component
    /// catalog. Safe to place ahead of a cache breakpoint.
    /// </summary>
    Stable,

    /// <summary>
    /// Rewritten on some or every turn: the canvas state, which is re-exported at each mint.
    /// A single volatile byte ahead of a breakpoint invalidates the whole cached prefix, so
    /// these always sort to the tail.
    /// </summary>
    Volatile,
}

/// <summary>
/// One labelled section of a system prompt, together with how it behaves across turns.
/// </summary>
/// <param name="Text">The section text. Empty or whitespace-only sections are dropped on assembly.</param>
/// <param name="Stability">Whether the section is stable across the session or rewritten per turn.</param>
public sealed record SystemPromptSegment(string Text, SystemPromptStability Stability);

/// <summary>
/// A system prompt held as ordered segments rather than one flat string, so a provider can put a
/// cache breakpoint between the part that never changes and the part that changes every turn.
///
/// <para>This exists because the flat-string form made caching impossible. Physalia re-exports the
/// live canvas as GhJSON into the system prompt at every mint, so the assembled prompt differs on
/// every turn — and a provider seeing one opaque string has no way to know that its first ~80% was
/// byte-identical to last turn's. Measured on a real session: ~125,000 chars of system prompt
/// re-sent at full price across 42 calls, about 82% of all input tokens, of which the preamble +
/// schema + component catalog never changed once.</para>
///
/// <para>The invariant is enforced here rather than at the call site: construction sorts every
/// stable segment ahead of every volatile one (preserving relative order within each group), so a
/// caller that assembles groundings in arbitrary wire order still gets a cacheable prefix.</para>
/// </summary>
public sealed record SystemPrompt
{
    /// <summary>
    /// Below this, a cache breakpoint is not worth taking: writing a cache entry costs a premium
    /// over the base input rate, so a prefix that is never re-read is a pure loss.
    ///
    /// <para>This is a cheapness gate, not a correctness one. The provider's own minimum cacheable
    /// prefix is model-dependent (roughly 512–4096 tokens, and NOT monotonic across generations),
    /// and falling under it is silent — the request succeeds and simply reports no cache write.
    /// Marking a too-short prefix is therefore harmless, so rather than track a per-model floor
    /// that would have to be maintained in a second place, this sits at a value comfortably above
    /// the small end and lets the provider decide the rest. Physalia's real stable prefix — the
    /// preamble, schema and component catalog — runs to tens of thousands of tokens, far above
    /// every model's floor.</para>
    /// </summary>
    private const int MinCacheableChars = 4096;

    private const string Separator = "\n\n";

    /// <summary>
    /// An empty prompt — no segments, no text, nothing cacheable.
    /// </summary>
    public static readonly SystemPrompt Empty = new(Array.Empty<SystemPromptSegment>());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPrompt"/> class from ordered segments.
    /// Empty and whitespace-only segments are dropped; the survivors are reordered stable-first.
    /// </summary>
    /// <param name="segments">The segments to assemble, in their authored order.</param>
    public SystemPrompt(IEnumerable<SystemPromptSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        List<SystemPromptSegment> kept = segments
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => s with { Text = s.Text.Trim() })
            .ToList();

        // Stable before volatile, relative order preserved inside each group. OrderBy is a stable
        // sort in LINQ-to-objects, which is exactly the guarantee this relies on.
        Segments = kept
            .OrderBy(s => s.Stability == SystemPromptStability.Stable ? 0 : 1)
            .ToList();

        var stable = Segments.Where(s => s.Stability == SystemPromptStability.Stable).ToList();

        Text = string.Join(Separator, Segments.Select(s => s.Text));

        // The stable prefix ends at the last stable segment. The separator that follows it belongs
        // to the prefix too: including it means the cached span ends on a boundary the next turn
        // reproduces byte-for-byte regardless of what the volatile tail says.
        StableCharCount = stable.Count == 0
            ? 0
            : string.Join(Separator, stable.Select(s => s.Text)).Length
              + (stable.Count == Segments.Count ? 0 : Separator.Length);
    }

    /// <summary>
    /// Gets the assembled segments, stable ones first.
    /// </summary>
    public IReadOnlyList<SystemPromptSegment> Segments { get; }

    /// <summary>
    /// Gets the whole prompt as one string — segments joined by a blank line. This is what a
    /// provider sends when it has no cache support and what every token estimator measures.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the length in characters of the stable prefix within <see cref="Text"/>. Zero when
    /// nothing is stable.
    /// </summary>
    public int StableCharCount { get; }

    /// <summary>
    /// Gets a value indicating whether this prompt is worth splitting at a cache breakpoint: it
    /// has a stable prefix long enough to pay for itself. A wholly stable prompt qualifies too —
    /// the breakpoint then sits at the very end, and the cached span covers everything.
    /// </summary>
    public bool HasCacheBreakpoint => StableCharCount >= MinCacheableChars;

    /// <summary>
    /// Gets the cacheable prefix — the stable segments and the separator that closes them.
    /// Meaningful only when <see cref="HasCacheBreakpoint"/> is true.
    /// </summary>
    public string StablePrefix => Text[..StableCharCount];

    /// <summary>
    /// Gets the per-turn tail that follows the cache breakpoint, empty when nothing is volatile.
    /// Meaningful only when <see cref="HasCacheBreakpoint"/> is true.
    /// </summary>
    public string VolatileSuffix => Text[StableCharCount..];

    /// <summary>
    /// Gets a value indicating whether the prompt carries no text at all.
    /// </summary>
    public bool IsEmpty => Text.Length == 0;

    /// <summary>
    /// Adopts a plain string as a single stable segment, so every existing caller that passes a
    /// bare prompt keeps working and gets caching for free.
    /// </summary>
    /// <param name="text">The prompt text, or null for an empty prompt.</param>
    public static implicit operator SystemPrompt(string? text) => FromText(text);

    /// <summary>
    /// Builds a prompt from one plain string, treated as wholly stable.
    /// </summary>
    /// <param name="text">The prompt text, or null for an empty prompt.</param>
    /// <returns>The assembled prompt.</returns>
    public static SystemPrompt FromText(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Empty
            : new SystemPrompt(new[] { new SystemPromptSegment(text!, SystemPromptStability.Stable) });

    /// <inheritdoc/>
    public override string ToString() => Text;
}
