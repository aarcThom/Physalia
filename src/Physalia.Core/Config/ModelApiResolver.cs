// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Config;

/// <summary>
/// The single read path for provider endpoints and credentials.
/// </summary>
/// <remarks>
/// <para>Everything that needs a credential goes through here — the Model API component, the web
/// tools, and the chat window's "which providers are configured" probe — so the three cannot
/// disagree about what is set up.</para>
/// <para><b>Resolution order, and why each step earns its place:</b></para>
/// <list type="number">
/// <item><b>Environment variable.</b> First. It is the path for headless and CI use, for shared team
/// setups, and for anyone who wants no credential on disk at all — strictly better than any at-rest
/// encryption, and it works identically on every platform. Variable names come from
/// <see cref="ProviderCatalog"/>.</item>
/// <item><b>The encrypted credential store</b>, written by the setup page.</item>
/// </list>
/// <para>There is no third source. A plain-text YAML fallback existed briefly and was removed along
/// with the file itself — with no released version to migrate from, it was a second way to configure
/// the same thing, and two of those disagree eventually.</para>
/// <para>An endpoint and a key resolve INDEPENDENTLY: a key from the environment still picks up a
/// custom endpoint from the store, which is the combination someone pointing at a private gateway
/// with a shell-managed token actually wants.</para>
/// <para><b>Finding a credential is not the same as being allowed to use it.</b>
/// <see cref="Resolve"/> answers only for providers the user has CONNECTED
/// (<see cref="ProviderActivation"/>); an unconnected one resolves to null however available it is.
/// <see cref="StatusFor"/> is the un-gated view the setup page needs, so a key sitting in the
/// environment can be offered — "found in GEMINI_API_KEY, add to Physalia" — rather than either
/// ignored or silently adopted.</para>
/// </remarks>
public sealed class ModelApiResolver
{
    private readonly CredentialStore _store;
    private readonly ProviderActivation _activation;
    private readonly Func<string, string?> _environment;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelApiResolver"/> class.
    /// </summary>
    /// <param name="store">The encrypted credential store.</param>
    /// <param name="activation">Which providers the user has connected.</param>
    /// <param name="environment">
    /// How to read an environment variable. Injected so the resolution order can be tested at all:
    /// with the real environment, a machine that happens to have <c>OPENAI_API_KEY</c> set makes a
    /// test pass or fail for reasons that have nothing to do with the code. Null uses the process
    /// environment, which is what every caller outside a test wants.
    /// </param>
    public ModelApiResolver(
        CredentialStore store,
        ProviderActivation activation,
        Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(activation);
        this._store = store;
        this._activation = activation;
        this._environment = environment ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>
    /// Gets the reason stored credentials could not be read, or null when there is no such problem.
    /// </summary>
    public string? UnreadableReason => this._store.UnreadableReason;

    /// <summary>
    /// Resolves one provider.
    /// </summary>
    /// <param name="providerId">The provider id, as listed in <see cref="ProviderCatalog"/>.</param>
    /// <returns>
    /// The resolved endpoint and credential, or null when nothing configures this provider.
    /// </returns>
    public ModelApi? Resolve(string providerId)
    {
        ProviderInfo? info = ProviderCatalog.Find(providerId);
        if (info is null || !this._activation.IsActivated(info.Id))
            return null;

        return this.Find(info).Api;
    }

    /// <summary>
    /// Reports one provider's state for the setup page - what is available, from where, and whether
    /// the user has connected it.
    /// </summary>
    /// <remarks>
    /// Un-gated by activation on purpose. This is what lets the page distinguish "no key anywhere"
    /// (show the form) from "a key is sitting in your environment" (show one button), which is the
    /// whole reason availability and consent are tracked separately.
    /// </remarks>
    /// <param name="providerId">The provider id.</param>
    /// <returns>The status, or null when the id is not one Physalia knows.</returns>
    public ProviderStatus? StatusFor(string providerId)
    {
        ProviderInfo? info = ProviderCatalog.Find(providerId);
        if (info is null)
            return null;

        // A probed provider has no credential to find; the caller supplies its detection result.
        if (info.Auth == ProviderAuth.Detected)
            return new ProviderStatus(info.Id, this._activation.IsActivated(info.Id), ProviderSource.None, null);

        (ModelApi? api, ProviderSource source, string? detail) = this.Find(info);
        return new ProviderStatus(
            info.Id,
            this._activation.IsActivated(info.Id),
            api is null ? ProviderSource.None : source,
            detail);
    }

    /// <summary>
    /// Reports every credentialed provider's state.
    /// </summary>
    /// <returns>One status per storable provider, in catalog order.</returns>
    public IReadOnlyList<ProviderStatus> Statuses() =>
        ProviderCatalog.Credentialed()
            .Select(info => this.StatusFor(info.Id))
            .Where(status => status is not null)
            .Select(status => status!.Value)
            .ToList();

    // The credential itself, ignoring activation: environment first, then the store. The endpoint is
    // resolved independently of the key, so a shell-managed token still picks up a stored endpoint.
    private (ModelApi? Api, ProviderSource Source, string? Detail) Find(ProviderInfo info)
    {
        ModelApi? stored = this._store.Get(info.Id);
        (string? envKey, string? envVar) = this.EnvironmentKey(info);

        string key = envKey ?? stored?.Key ?? string.Empty;
        string url = stored?.BaseUrl is { Length: > 0 } storedUrl ? storedUrl : info.DefaultBaseUrl;

        // A provider is available when it has a credential, OR when the user deliberately stored an
        // endpoint for it. The second case is not an edge: a local runtime behind a URL and no key at
        // all is a perfectly ordinary setup.
        if (string.IsNullOrWhiteSpace(key) && stored is null)
            return (null, ProviderSource.None, null);

        ProviderSource source = envKey is not null ? ProviderSource.Environment : ProviderSource.Stored;
        return (new ModelApi(info.Id, url, key), source, envKey is not null ? envVar : null);
    }

    /// <summary>
    /// Resolves every provider the user has connected.
    /// </summary>
    /// <returns>The resolved providers, in catalog order.</returns>
    public IReadOnlyList<ModelApi> All() =>
        ProviderCatalog.Credentialed()
            .Select(info => this.Resolve(info.Id))
            .Where(api => api is not null)
            .Select(api => api!)
            .ToList();

    /// <summary>
    /// Returns the ids of every configured provider of a given kind.
    /// </summary>
    /// <param name="kind">Chat providers or web-tool keys.</param>
    /// <returns>The matching provider ids, in catalog order.</returns>
    public IReadOnlyList<string> ConfiguredIds(ProviderKind kind) =>
        ProviderCatalog.Credentialed()
            .Where(info => info.Kind == kind && this.Resolve(info.Id) is not null)
            .Select(info => info.Id)
            .ToList();

    // First non-empty environment variable named by the catalog entry, with the name it came from -
    // the setup page shows it, so "found in GOOGLE_API_KEY" is actionable rather than mysterious.
    private (string? Key, string? Variable) EnvironmentKey(ProviderInfo info)
    {
        foreach (string name in info.EnvVars)
        {
            string? value = this._environment(name);
            if (!string.IsNullOrWhiteSpace(value))
                return (value, name);
        }

        return (null, null);
    }
}
