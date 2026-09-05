// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Physalia.Core.Config;
using Xunit;

namespace Physalia.Core.Tests.Config;

/// <summary>
/// Covers the opt-in layer: a provider being AVAILABLE (a key in the environment, a CLI on PATH) is
/// never the same as the user having CONNECTED it.
/// </summary>
/// <remarks>
/// This separation exists because the previous behaviour treated any resolvable key as
/// configuration, so a machine with unrelated tooling installed arrived pre-wired to providers
/// nobody had chosen.
/// </remarks>
public class ProviderActivationTests : IDisposable
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

    [Fact]
    public void A_provider_starts_unconnected()
    {
        Assert.False(this.NewActivation().IsActivated("anthropic"));
    }

    [Fact]
    public void Activate_then_deactivate_round_trips()
    {
        ProviderActivation activation = this.NewActivation();

        activation.Activate("anthropic");
        Assert.True(activation.IsActivated("anthropic"));
        Assert.Contains("anthropic", activation.ActivatedIds());

        activation.Deactivate("anthropic");
        Assert.False(activation.IsActivated("anthropic"));
        Assert.Empty(activation.ActivatedIds());
    }

    [Fact]
    public void Activation_survives_a_reload()
    {
        string path = this.TempPath();

        new ProviderActivation(path).Activate("codex");

        Assert.True(new ProviderActivation(path).IsActivated("codex"));
    }

    [Fact]
    public void A_corrupt_file_reads_as_nothing_connected()
    {
        // Recoverable by re-connecting, and the setup screen already renders "nothing connected".
        string path = this.TempPath();
        File.WriteAllText(path, "{ not json at all");

        Assert.False(new ProviderActivation(path).IsActivated("anthropic"));
    }

    [Fact]
    public void An_environment_key_alone_does_not_configure_a_provider()
    {
        // The headline behaviour: GEMINI_API_KEY exported for some other tool must not silently
        // enrol Google.
        var resolver = new ModelApiResolver(
            new CredentialStore(new FakeSecretStore()),
            this.NewActivation(),
            name => name == "GEMINI_API_KEY" ? "AIza-x" : null);

        Assert.Null(resolver.Resolve("google"));
        Assert.Empty(resolver.ConfiguredIds(ProviderKind.Llm));
    }

    [Fact]
    public void That_same_key_is_reported_as_available_so_the_page_can_offer_it()
    {
        // Not configured, but the setup page must still be able to say "found in GEMINI_API_KEY" and
        // put one button under it — otherwise the key may as well not exist.
        ProviderStatus? status = new ModelApiResolver(
                new CredentialStore(new FakeSecretStore()),
                this.NewActivation(),
                name => name == "GEMINI_API_KEY" ? "AIza-x" : null)
            .StatusFor("google");

        Assert.NotNull(status);
        Assert.True(status!.Value.Available);
        Assert.False(status.Value.Activated);
        Assert.False(status.Value.Ready);
        Assert.Equal(ProviderSource.Environment, status.Value.Source);
        Assert.Equal("GEMINI_API_KEY", status.Value.Detail);
    }

    [Fact]
    public void Connecting_it_makes_it_resolve()
    {
        ProviderActivation activation = this.NewActivation();
        var resolver = new ModelApiResolver(
            new CredentialStore(new FakeSecretStore()),
            activation,
            name => name == "GEMINI_API_KEY" ? "AIza-x" : null);

        activation.Activate("google");

        Assert.Equal("AIza-x", resolver.Resolve("google")!.Key);
        Assert.Contains("google", resolver.ConfiguredIds(ProviderKind.Llm));
    }

    [Fact]
    public void A_stored_key_still_needs_activation_to_resolve()
    {
        // The setup page activates on save, so in practice these arrive together — but the gate is
        // the store's business, not the caller's, and must hold on its own.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("openai", "https://api.openai.com/v1", "sk-1"));

        var resolver = new ModelApiResolver(store, this.NewActivation(), NoEnvironment);

        Assert.Null(resolver.Resolve("openai"));
        Assert.Equal(ProviderSource.Stored, resolver.StatusFor("openai")!.Value.Source);
    }

    [Fact]
    public void Connecting_a_provider_with_nothing_behind_it_is_still_not_ready()
    {
        // Consent without a credential configures nothing — a stale entry for a key that has since
        // been unset must not report the provider as usable.
        ProviderActivation activation = this.NewActivation();
        activation.Activate("openai");

        var resolver = new ModelApiResolver(
            new CredentialStore(new FakeSecretStore()), activation, NoEnvironment);

        Assert.Null(resolver.Resolve("openai"));
        Assert.False(resolver.StatusFor("openai")!.Value.Ready);
    }

    [Fact]
    public void A_probed_provider_reports_activation_but_never_a_credential_source()
    {
        // Core cannot see a PATH; the GH layer supplies the detection half. What Core owns is whether
        // the user connected it.
        ProviderActivation activation = this.NewActivation();
        activation.Activate("claude-code");

        ProviderStatus? status = new ModelApiResolver(
            new CredentialStore(new FakeSecretStore()), activation, NoEnvironment).StatusFor("claude-code");

        Assert.True(status!.Value.Activated);
        Assert.Equal(ProviderSource.None, status.Value.Source);
    }

    private string TempPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"phy-act-{Guid.NewGuid():N}.json");
        this._tempFiles.Add(path);
        return path;
    }

    private ProviderActivation NewActivation() => new(this.TempPath());
}
