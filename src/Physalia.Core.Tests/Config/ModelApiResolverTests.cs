// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Physalia.Core.Config;
using Xunit;

namespace Physalia.Core.Tests.Config;

/// <summary>
/// Covers the resolution order — environment variable, then the encrypted store — and the
/// independence of the endpoint from the key.
/// </summary>
/// <remarks>
/// The environment is INJECTED rather than mutated. Reading the real one made these tests depend on
/// whatever the developer happened to have exported: a machine with <c>OPENAI_API_KEY</c> set failed
/// a test about Tavily, which is exactly the kind of noise that gets a suite ignored.
/// </remarks>
public class ModelApiResolverTests : IDisposable
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (string path in this._tempFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        GC.SuppressFinalize(this);
    }

    // A real activation list on a throwaway file. Connecting is a deliberate act everywhere in these
    // tests, because that is exactly the behaviour under test: availability alone resolves to null.
    private ProviderActivation Connected(params string[] providerIds)
    {
        string path = Path.Combine(Path.GetTempPath(), $"phy-act-{Guid.NewGuid():N}.json");
        this._tempFiles.Add(path);

        var activation = new ProviderActivation(path);
        foreach (string id in providerIds)
            activation.Activate(id);

        return activation;
    }

    private static Func<string, string?> Env(params (string Name, string Value)[] entries)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in entries)
            map[name] = value;

        return name => map.TryGetValue(name, out string? value) ? value : null;
    }

    [Fact]
    public void A_connected_provider_with_no_credential_anywhere_resolves_to_null()
    {
        var resolver = new ModelApiResolver(new CredentialStore(new FakeSecretStore()), this.Connected("anthropic"), NoEnvironment);

        Assert.Null(resolver.Resolve("anthropic"));
    }

    [Fact]
    public void An_unknown_provider_id_resolves_to_null()
    {
        var resolver = new ModelApiResolver(new CredentialStore(new FakeSecretStore()), this.Connected(), NoEnvironment);

        Assert.Null(resolver.Resolve("not-a-provider"));
    }

    [Fact]
    public void The_store_supplies_the_key_when_no_environment_variable_is_set()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("anthropic", "https://api.anthropic.com/v1", "sk-stored"));

        var resolver = new ModelApiResolver(store, this.Connected("anthropic"), NoEnvironment);
        Assert.Equal("sk-stored", resolver.Resolve("anthropic")!.Key);
    }

    [Fact]
    public void The_environment_variable_wins_over_the_store()
    {
        // Keeping a credential out of any file at all beats encrypting it, so this order does not
        // change: env first, always.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("anthropic", "https://api.anthropic.com/v1", "sk-stored"));

        var resolver = new ModelApiResolver(store, this.Connected("anthropic"), Env(("ANTHROPIC_API_KEY", "sk-from-env")));
        Assert.Equal("sk-from-env", resolver.Resolve("anthropic")!.Key);
    }

    [Fact]
    public void A_secondary_environment_variable_name_is_honoured()
    {
        // Google publishes both GEMINI_API_KEY and GOOGLE_API_KEY; the catalog lists them in order.
        var resolver = new ModelApiResolver(
            new CredentialStore(new FakeSecretStore()), this.Connected("google"), Env(("GOOGLE_API_KEY", "AIza-x")));

        Assert.Equal("AIza-x", resolver.Resolve("google")!.Key);
    }

    [Fact]
    public void A_key_from_the_environment_still_picks_up_a_custom_endpoint_from_the_store()
    {
        // The combination someone pointing at a private gateway with a shell-managed token wants:
        // the two halves resolve independently.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("anthropic", "https://gateway.internal/v1", string.Empty));

        var resolver = new ModelApiResolver(store, this.Connected("anthropic"), Env(("ANTHROPIC_API_KEY", "sk-from-env")));
        ModelApi? resolved = resolver.Resolve("anthropic");

        Assert.Equal("sk-from-env", resolved!.Key);
        Assert.Equal("https://gateway.internal/v1", resolved.BaseUrl);
    }

    [Fact]
    public void An_unconfigured_endpoint_falls_back_to_the_catalog_default()
    {
        var resolver = new ModelApiResolver(
            new CredentialStore(new FakeSecretStore()), this.Connected("anthropic"), Env(("ANTHROPIC_API_KEY", "sk-from-env")));

        Assert.Equal("https://api.anthropic.com/v1", resolver.Resolve("anthropic")!.BaseUrl);
    }

    [Fact]
    public void Configured_ids_separate_chat_providers_from_web_tool_keys()
    {
        // A Tavily key alone must not satisfy the first-run requirement: it can call no model.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("tavily", string.Empty, "tvly-1"));

        var resolver = new ModelApiResolver(store, this.Connected("tavily"), NoEnvironment);

        Assert.Empty(resolver.ConfiguredIds(ProviderKind.Llm));
        Assert.Equal(new[] { "tavily" }, resolver.ConfiguredIds(ProviderKind.Tool));
    }

    [Fact]
    public void A_detected_provider_is_never_resolved_as_a_credential()
    {
        // Claude Code, Codex and the local server are probed, never stored — asking the resolver
        // about one must not invent a configuration for it.
        var resolver = new ModelApiResolver(
            new CredentialStore(new FakeSecretStore()), this.Connected("claude-code", "codex", "local-llm"), NoEnvironment);

        Assert.Null(resolver.Resolve("claude-code"));
        Assert.Null(resolver.Resolve("codex"));
        Assert.DoesNotContain("local-llm", resolver.ConfiguredIds(ProviderKind.Llm));
    }

    [Fact]
    public void The_status_reports_the_endpoint_in_effect_so_an_edit_starts_where_it_left_off()
    {
        // The setup page prefills its URL box from this. A Z.AI Coding Plan key needs a different
        // host from the general one, so showing the catalog default to someone who has already moved
        // off it invites them to save it back.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("zai", "https://api.z.ai/api/coding/paas/v4", "sk-coding"));

        var resolver = new ModelApiResolver(store, this.Connected("zai"), NoEnvironment);

        Assert.Equal("https://api.z.ai/api/coding/paas/v4", resolver.StatusFor("zai")!.Value.BaseUrl);
    }

    [Fact]
    public void The_status_endpoint_falls_back_to_the_catalog_default()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("anthropic", string.Empty, "sk-stored"));

        var resolver = new ModelApiResolver(store, this.Connected("anthropic"), NoEnvironment);

        Assert.Equal("https://api.anthropic.com/v1", resolver.StatusFor("anthropic")!.Value.BaseUrl);
    }

    [Fact]
    public void The_status_reports_that_a_key_is_stored_without_carrying_the_key()
    {
        // What the page is told is the FACT of a key on disk, which is what lets a blank key box mean
        // "leave the stored one alone" and tells Disconnect it has a secret to destroy.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("anthropic", string.Empty, "sk-stored"));

        var resolver = new ModelApiResolver(store, this.Connected("anthropic"), NoEnvironment);

        Assert.True(resolver.StatusFor("anthropic")!.Value.HasStoredKey);
    }

    [Fact]
    public void A_key_living_only_in_the_environment_is_not_reported_as_stored()
    {
        // An environment key is not Physalia's to keep or delete, so disconnecting must not offer to
        // destroy it — availability and ownership are different questions.
        var resolver = new ModelApiResolver(
            new CredentialStore(new FakeSecretStore()),
            this.Connected("anthropic"),
            Env(("ANTHROPIC_API_KEY", "sk-from-env")));

        ProviderStatus status = resolver.StatusFor("anthropic")!.Value;

        Assert.Equal(ProviderSource.Environment, status.Source);
        Assert.False(status.HasStoredKey);
    }

    [Fact]
    public void A_stored_endpoint_with_no_key_anywhere_reports_no_stored_key()
    {
        // The key-less local-runtime case: available because an endpoint was deliberately stored,
        // with nothing to forget.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("other", "http://localhost:11434/v1", string.Empty));

        var resolver = new ModelApiResolver(store, this.Connected("other"), NoEnvironment);
        ProviderStatus status = resolver.StatusFor("other")!.Value;

        Assert.Equal(ProviderSource.Stored, status.Source);
        Assert.Equal("http://localhost:11434/v1", status.BaseUrl);
        Assert.False(status.HasStoredKey);
    }
}
