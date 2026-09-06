// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Config;

namespace Physalia.Core.Api;

/// <summary>
/// Where an API endpoint's key comes from: the environment variable the endpoint names, then the
/// encrypted credential store.
/// </summary>
/// <remarks>
/// <para>The same two sources, in the same order, as the model providers use, and for the same
/// reason: no credential on disk at all beats any encryption, and naming a variable is the headless,
/// CI and shared-team path.</para>
/// <para><b>No activation gate here, deliberately.</b> A model provider can be found already
/// configured on a machine — a key in the environment, a CLI on PATH — which is why availability had
/// to be separated from consent there. Nothing discovers an API endpoint: the user typed it into the
/// setup page, and typing it in IS the opt-in. Adding a second list to tick would be ceremony over a
/// decision already made.</para>
/// <para>The environment lookup is injected for the reason it is on the model resolver: reading the
/// real environment makes the resolution order untestable, since a machine that happens to have the
/// named variable set decides the test.</para>
/// </remarks>
public sealed class ApiKeyResolver
{
    private readonly CredentialStore _store;
    private readonly Func<string, string?> _environment;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyResolver"/> class.
    /// </summary>
    /// <param name="store">The encrypted credential store the setup page writes keys to.</param>
    /// <param name="environment">
    /// How to read an environment variable. Null uses the process environment, which is what every
    /// caller outside a test wants.
    /// </param>
    public ApiKeyResolver(CredentialStore store, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this._store = store;
        this._environment = environment ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>
    /// Resolves the key for one endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint whose key is wanted.</param>
    /// <returns>
    /// The key, or null when the endpoint needs none or none is configured. A null is not an error:
    /// an open-data portal is reached without one.
    /// </returns>
    public string? Resolve(ApiEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.NeedsKey)
            return null;

        if (!string.IsNullOrWhiteSpace(endpoint.EnvVar))
        {
            string? fromEnvironment = this._environment(endpoint.EnvVar);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return fromEnvironment;
        }

        string? stored = this._store.Get(endpoint.CredentialId)?.Key;
        return string.IsNullOrWhiteSpace(stored) ? null : stored;
    }

    /// <summary>
    /// Reports where an endpoint's key would come from, for the setup page.
    /// </summary>
    /// <param name="endpoint">The endpoint to report on.</param>
    /// <returns>
    /// The name of the environment variable supplying it, "stored" when the credential store does,
    /// or null when the endpoint has no key.
    /// </returns>
    public string? SourceOf(ApiEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.NeedsKey)
            return null;

        if (!string.IsNullOrWhiteSpace(endpoint.EnvVar)
            && !string.IsNullOrWhiteSpace(this._environment(endpoint.EnvVar)))
        {
            return endpoint.EnvVar;
        }

        return string.IsNullOrWhiteSpace(this._store.Get(endpoint.CredentialId)?.Key) ? null : "stored";
    }
}
