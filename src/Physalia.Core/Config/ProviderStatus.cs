// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Config;

/// <summary>
/// Where a provider's credential was found, or how its availability was established.
/// </summary>
public enum ProviderSource
{
    /// <summary>
    /// Nothing configures this provider — no key anywhere, or nothing answering a probe.
    /// </summary>
    None,

    /// <summary>
    /// A key is present in an environment variable. Present is not the same as connected: the user
    /// still opts in, because a variable exported for some other tool is not a decision to let this
    /// plug-in spend that quota.
    /// </summary>
    Environment,

    /// <summary>
    /// A key and/or endpoint the user entered on the setup page, in the encrypted store.
    /// </summary>
    Stored,

    /// <summary>
    /// Found by probing — a CLI on PATH, or a local server answering. Nothing is stored.
    /// </summary>
    Detected,
}

/// <summary>
/// What the setup page needs to know about one provider: whether it could be used, how, and whether
/// the user has actually connected it.
/// </summary>
/// <remarks>
/// The three states the UI renders come from the pair of flags: <c>Source == None</c> means "not set
/// up" (show the form or the install steps), available-but-not-<see cref="Activated"/> means "found,
/// one button to connect", and <see cref="Ready"/> means "connected" (show a pill).
/// </remarks>
/// <param name="Id">The provider id, matching a <see cref="ProviderCatalog"/> entry.</param>
/// <param name="Activated">Whether the user has connected this provider to Physalia.</param>
/// <param name="Source">Where the credential or detection came from.</param>
/// <param name="Detail">
/// A short human-readable specific — the environment variable a key was found in, say — or null.
/// Lets the page say "found in GEMINI_API_KEY" instead of just "found".
/// </param>
public readonly record struct ProviderStatus(
    string Id,
    bool Activated,
    ProviderSource Source,
    string? Detail)
{
    /// <summary>
    /// Gets a value indicating whether this provider could be used if the user connected it.
    /// </summary>
    public bool Available => this.Source != ProviderSource.None;

    /// <summary>
    /// Gets a value indicating whether the pipeline may actually use this provider — available AND
    /// connected. This, never <see cref="Available"/> alone, is what counts as configured.
    /// </summary>
    public bool Ready => this.Activated && this.Available;
}
