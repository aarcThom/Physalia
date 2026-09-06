// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Physalia.Core.Api;
using Physalia.Core.Config;
using Physalia.Core.Tests.Config;
using Xunit;

namespace Physalia.Core.Tests.Api;

/// <summary>
/// Covers where an API endpoint's key comes from: the named environment variable first, then the
/// encrypted store.
/// </summary>
/// <remarks>
/// The environment is injected rather than read, for the reason the model resolver injects it: with
/// the real one, a machine that happens to have the named variable set decides the test.
/// </remarks>
public class ApiKeyResolverTests
{
    [Fact]
    public void An_open_endpoint_resolves_to_no_key()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("api:open", string.Empty, "should-be-ignored"));

        var resolver = new ApiKeyResolver(store, _ => "also-ignored");

        Assert.Null(resolver.Resolve(new ApiEndpoint("open", "https://example.com/")));
    }

    [Fact]
    public void The_environment_variable_wins_over_the_store()
    {
        // Keeping a credential off disk entirely beats any at-rest encryption, so it stays first.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("api:vancouver", string.Empty, "from-store"));

        var resolver = new ApiKeyResolver(store, name => name == "VAN_KEY" ? "from-env" : null);

        Assert.Equal("from-env", resolver.Resolve(Keyed("VAN_KEY")));
        Assert.Equal("VAN_KEY", resolver.SourceOf(Keyed("VAN_KEY")));
    }

    [Fact]
    public void The_store_answers_when_the_variable_is_unset()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("api:vancouver", string.Empty, "from-store"));

        var resolver = new ApiKeyResolver(store, _ => null);

        Assert.Equal("from-store", resolver.Resolve(Keyed("VAN_KEY")));
        Assert.Equal("stored", resolver.SourceOf(Keyed("VAN_KEY")));
    }

    [Fact]
    public void A_blank_environment_value_is_not_a_key()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("api:vancouver", string.Empty, "from-store"));

        var resolver = new ApiKeyResolver(store, _ => "   ");

        Assert.Equal("from-store", resolver.Resolve(Keyed("VAN_KEY")));
    }

    [Fact]
    public void An_endpoint_naming_no_variable_goes_straight_to_the_store()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("api:vancouver", string.Empty, "from-store"));

        var resolver = new ApiKeyResolver(store, _ => throw new InvalidOperationException("must not be consulted"));

        Assert.Equal("from-store", resolver.Resolve(Keyed(envVar: string.Empty)));
    }

    [Fact]
    public void Nothing_configured_resolves_to_null()
    {
        var resolver = new ApiKeyResolver(new CredentialStore(new FakeSecretStore()), _ => null);

        Assert.Null(resolver.Resolve(Keyed("VAN_KEY")));
        Assert.Null(resolver.SourceOf(Keyed("VAN_KEY")));
    }

    private static ApiEndpoint Keyed(string envVar) =>
        new("vancouver", "https://example.com/", ApiAuth.BearerHeader, string.Empty, string.Empty, envVar);
}
