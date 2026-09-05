// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Config;

namespace Physalia.GH.Config;

/// <summary>
/// The plug-in's one credential store, activation list and resolver.
/// </summary>
/// <remarks>
/// Shared process-wide on purpose. The store decrypts once and caches; handing every component its
/// own instance would mean a decrypt per node per solve, and would let two nodes disagree about what
/// is configured after a save.
/// </remarks>
internal static class PhyCredentials
{
    private static readonly CredentialStore StoreInstance = CredentialStore.Default();
    private static readonly ProviderActivation ActivationInstance = ProviderActivation.Default();

    /// <summary>
    /// Gets the encrypted credential store the setup page writes to.
    /// </summary>
    internal static CredentialStore Store => StoreInstance;

    /// <summary>
    /// Gets the list of providers the user has connected. Separate from the store because
    /// availability is not consent: a key in the environment says a provider COULD be used, not that
    /// it should be.
    /// </summary>
    internal static ProviderActivation Activation => ActivationInstance;

    /// <summary>
    /// Gets the resolver every credential read goes through (environment, then the store, gated on
    /// the provider having been connected).
    /// </summary>
    internal static ModelApiResolver Resolver { get; } = new(StoreInstance, ActivationInstance);

    /// <summary>
    /// Drops the cached reads so the next resolve sees a just-written change.
    /// </summary>
    internal static void Invalidate()
    {
        StoreInstance.Invalidate();
        ActivationInstance.Invalidate();
    }
}
