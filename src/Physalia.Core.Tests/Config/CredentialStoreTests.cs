// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Physalia.Core.Config;
using Physalia.Core.Config.Secrets;
using Xunit;

namespace Physalia.Core.Tests.Config;

/// <summary>
/// An in-memory <see cref="ISecretStore"/>, so the store's behaviour can be tested without DPAPI,
/// a real file, or a platform branch.
/// </summary>
/// <remarks>
/// That this is possible at all is the point of the interface: everything above
/// <see cref="ISecretStore"/> deals in a payload and a status, so the Mac implementation, when it
/// lands, changes nothing these tests assert.
/// </remarks>
internal sealed class FakeSecretStore : ISecretStore
{
    private string? _payload;

    internal FakeSecretStore(string? initial = null) => this._payload = initial;

    /// <summary>
    /// Gets or sets the status the next read reports. Lets a test stand in for a store written by
    /// another OS account without needing another OS account.
    /// </summary>
    internal SecretReadStatus NextStatus { get; set; } = SecretReadStatus.Ok;

    internal int Writes { get; private set; }

    public string Description => "held in memory for a test";

    public bool IsEncrypted => true;

    public SecretReadResult Read()
    {
        if (this.NextStatus == SecretReadStatus.Unreadable)
            return SecretReadResult.Unreadable("written by a different account");

        return this._payload is null ? SecretReadResult.Empty() : SecretReadResult.Ok(this._payload);
    }

    public void Write(string payload)
    {
        this._payload = payload;
        this.Writes++;
    }

    public void Delete() => this._payload = null;
}

public class CredentialStoreTests
{
    [Fact]
    public void Save_then_get_round_trips_url_and_key()
    {
        var store = new CredentialStore(new FakeSecretStore());

        store.Save(new ModelApi("moonshot", "https://api.moonshot.ai/v1", "sk-abc"));

        ModelApi? read = store.Get("moonshot");
        Assert.NotNull(read);
        Assert.Equal("https://api.moonshot.ai/v1", read!.BaseUrl);
        Assert.Equal("sk-abc", read.Key);
    }

    [Fact]
    public void Get_is_case_insensitive_on_provider_id()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("anthropic", "https://api.anthropic.com/v1", "sk-ant"));

        Assert.NotNull(store.Get("Anthropic"));
    }

    [Fact]
    public void Save_replaces_only_the_named_provider()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("openai", "https://api.openai.com/v1", "sk-1"));
        store.Save(new ModelApi("zai", "https://api.z.ai/api/paas/v4", "sk-2"));
        store.Save(new ModelApi("openai", "https://api.openai.com/v1", "sk-3"));

        Assert.Equal("sk-3", store.Get("openai")!.Key);
        Assert.Equal("sk-2", store.Get("zai")!.Key);
        Assert.Equal(2, store.All().Count);
    }

    [Fact]
    public void An_entry_may_carry_an_endpoint_and_no_key()
    {
        // A local runtime behind a URL that wants no credential is an ordinary setup, not an edge.
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("other", "http://localhost:11434/v1", string.Empty));

        ModelApi? read = store.Get("other");
        Assert.NotNull(read);
        Assert.False(read!.HasKey);
        Assert.True(read.HasBaseUrl);
    }

    [Fact]
    public void Remove_drops_the_entry()
    {
        var store = new CredentialStore(new FakeSecretStore());
        store.Save(new ModelApi("openai", "https://api.openai.com/v1", "sk-1"));
        store.Remove("openai");

        Assert.Null(store.Get("openai"));
    }

    [Fact]
    public void An_unreadable_store_reports_a_reason_rather_than_looking_empty()
    {
        // The distinction the whole design turns on: "nothing configured" would send the user off to
        // re-enter keys that are sitting right there, merely encrypted for another account.
        var backing = new FakeSecretStore { NextStatus = SecretReadStatus.Unreadable };
        var store = new CredentialStore(backing);

        Assert.NotNull(store.UnreadableReason);
        Assert.Empty(store.All());
    }

    [Fact]
    public void Saving_over_an_unreadable_store_is_refused()
    {
        // Writing one entry onto a store we could not read would silently discard every OTHER
        // provider its real owner had configured.
        var backing = new FakeSecretStore { NextStatus = SecretReadStatus.Unreadable };
        var store = new CredentialStore(backing);

        Assert.Throws<InvalidOperationException>(() =>
            store.Save(new ModelApi("openai", "https://api.openai.com/v1", "sk-1")));
        Assert.Equal(0, backing.Writes);
    }

    [Fact]
    public void Unparseable_content_is_unreadable_not_empty()
    {
        var store = new CredentialStore(new FakeSecretStore("{ this is not json"));

        Assert.NotNull(store.UnreadableReason);
        Assert.Empty(store.All());
    }
}
