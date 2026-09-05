// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Config;

/// <summary>
/// One provider's endpoint and credential — everything needed to reach an API, as a single value.
/// </summary>
/// <remarks>
/// <para>Replaces the old <c>ApiKey</c> record, which carried a key with no endpoint. A key and the
/// URL it is valid at are one fact, not two settings a user assembles: an OpenAI key means nothing
/// at DeepSeek's endpoint, and the three providers added alongside this type — Alibaba, Z.AI and
/// Moonshot — are all OpenAI-compatible at DIFFERENT base URLs, so a key on its own does not
/// identify anything.</para>
/// <para>The secret never leaves this record as text: <c>GH_ModelApi</c> displays a label, refuses
/// to cast to a string, and writes nothing to a Grasshopper file.</para>
/// </remarks>
/// <param name="Provider">
/// The provider id, matching a <see cref="ProviderCatalog"/> entry and the setup screen's own ids
/// (e.g. "anthropic", "openai", "moonshot"). One vocabulary end to end.
/// </param>
/// <param name="BaseUrl">
/// The endpoint to call. Empty when the provider has no URL of its own (a web-tool key such as
/// Tavily) or when the caller should fall back to its config's built-in default.
/// </param>
/// <param name="Key">
/// The credential. May be empty: a local endpoint that asks for none is a normal configuration,
/// not an error.
/// </param>
public record ModelApi(string Provider, string BaseUrl, string Key)
{
    /// <summary>
    /// Gets a value indicating whether a credential is present.
    /// </summary>
    public bool HasKey => !string.IsNullOrWhiteSpace(this.Key);

    /// <summary>
    /// Gets a value indicating whether an endpoint is present.
    /// </summary>
    public bool HasBaseUrl => !string.IsNullOrWhiteSpace(this.BaseUrl);

    /// <summary>
    /// Returns this endpoint if one is set, otherwise the supplied fallback.
    /// </summary>
    /// <param name="fallback">
    /// The default to use when no endpoint was configured — normally the built-in
    /// <c>BaseUrl</c> of the target <c>ModelConfig</c>, so an unconfigured provider still reaches
    /// its own official API.
    /// </param>
    /// <returns>The endpoint to call.</returns>
    public string BaseUrlOr(string fallback) => this.HasBaseUrl ? this.BaseUrl : fallback;
}
