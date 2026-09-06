// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Api;

/// <summary>
/// How an API endpoint proves who is calling it.
/// </summary>
public enum ApiAuth
{
    /// <summary>
    /// Nothing is sent. The ordinary case for an open-data portal, and the reason the key box on
    /// the setup form may be left blank.
    /// </summary>
    None,

    /// <summary>
    /// <c>Authorization: Bearer &lt;key&gt;</c>. Sugar for the custom-header form, offered on its own
    /// because it is what most commercial APIs document.
    /// </summary>
    BearerHeader,

    /// <summary>
    /// A named header carrying the key, optionally behind a prefix — Opendatasoft's
    /// <c>Authorization: Apikey &lt;key&gt;</c>, or a bare <c>X-API-Key: &lt;key&gt;</c>.
    /// </summary>
    CustomHeader,

    /// <summary>
    /// The key rides in the query string under a named parameter.
    /// </summary>
    QueryParameter,
}

/// <summary>
/// One HTTP API the user has configured Physalia to call.
/// </summary>
/// <remarks>
/// <para><b>What is deliberately NOT here: the key, and the catalog.</b> The key is a secret, so it
/// lives in the encrypted credential store (see <see cref="ApiKeyResolver"/>) or in an environment
/// variable this entry merely names. The catalog and field list — what the model is told the API
/// holds — lives on the <c>API Call</c> node instead, because a setting is only useful if it
/// travels: this file is per-user and per-machine, so a pipeline shared as a preset would arrive
/// with its wiring and none of its knowledge. Same reasoning as the Memory tool's folder name and
/// the Read PDF folder, both typed on their node for exactly that reason.</para>
/// <para><b>Creating an entry IS the opt-in.</b> There is no activation list beside this one, and
/// none is wanted: a model provider can be found already configured on the machine (a key in the
/// environment, a CLI on PATH) which is why availability had to be separated from consent there.
/// Nothing discovers an API endpoint — the user typed it in, and typing it in is the consent.</para>
/// </remarks>
/// <param name="Name">
/// The entry's key, and what an <c>API Call</c> node stores to point at it. Also the namespace of
/// the advertised tool name, so two endpoints cannot collide at the Router.
/// </param>
/// <param name="BaseUrl">
/// The absolute http(s) root every call is composed against. A request may only reach paths beneath
/// it — see <see cref="ApiRequest"/>, which enforces that rather than trusting the composed URI.
/// </param>
/// <param name="Auth">How the key is presented, if at all.</param>
/// <param name="AuthName">
/// The header name (<see cref="ApiAuth.CustomHeader"/>) or query parameter name
/// (<see cref="ApiAuth.QueryParameter"/>). Ignored by the other forms.
/// </param>
/// <param name="AuthPrefix">
/// Text placed before the key in a custom header's value, e.g. <c>"Apikey "</c>. Empty sends the key
/// bare. Ignored by the other forms.
/// </param>
/// <param name="EnvVar">
/// An environment variable consulted for the key BEFORE the credential store. Naming one keeps the
/// secret off disk entirely, which is the headless, CI and shared-team path — the same order and the
/// same reasoning as <c>ModelApiResolver</c>.
/// </param>
public sealed record ApiEndpoint(
    string Name,
    string BaseUrl,
    ApiAuth Auth = ApiAuth.None,
    string AuthName = "",
    string AuthPrefix = "",
    string EnvVar = "")
{
    /// <summary>
    /// Gets the id this endpoint's key is stored under in the shared credential store.
    /// </summary>
    /// <remarks>
    /// Prefixed so an endpoint can never occupy the id of a model provider. The credential store
    /// keys by an arbitrary string and validates nothing, which is what makes reusing it here free —
    /// and reusing it is the point: one encryption seam in the repo, not a second one to keep in step.
    /// </remarks>
    public string CredentialId => "api:" + this.Name;

    /// <summary>
    /// Gets a value indicating whether this endpoint needs a key at all.
    /// </summary>
    public bool NeedsKey => this.Auth != ApiAuth.None;
}
