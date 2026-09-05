// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Config.Secrets;

/// <summary>
/// How a read of a secret store turned out.
/// </summary>
public enum SecretReadStatus
{
    /// <summary>
    /// The payload was read and decrypted.
    /// </summary>
    Ok,

    /// <summary>
    /// Nothing is stored yet. Not an error — it is what a first run looks like.
    /// </summary>
    Empty,

    /// <summary>
    /// Something IS stored but this process cannot decrypt it, most often because the store was
    /// written by a different OS user or on a different machine. Distinct from
    /// <see cref="Empty"/> on purpose: telling a user "nothing is configured" when their
    /// credentials are sitting right there, merely unreadable, sends them off to re-enter
    /// everything instead of signing in as the account that owns them.
    /// </summary>
    Unreadable,
}

/// <summary>
/// The outcome of <see cref="ISecretStore.Read"/>.
/// </summary>
/// <param name="Status">Whether the payload was read, absent, or undecryptable.</param>
/// <param name="Payload">The stored text when <paramref name="Status"/> is
/// <see cref="SecretReadStatus.Ok"/>; otherwise null.</param>
/// <param name="Reason">A human-readable explanation when the read did not succeed; otherwise null.</param>
public readonly record struct SecretReadResult(SecretReadStatus Status, string? Payload, string? Reason)
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="payload">The decrypted text.</param>
    /// <returns>An <see cref="SecretReadStatus.Ok"/> result.</returns>
    public static SecretReadResult Ok(string payload) => new(SecretReadStatus.Ok, payload, null);

    /// <summary>
    /// Creates a result for a store that holds nothing yet.
    /// </summary>
    /// <returns>An <see cref="SecretReadStatus.Empty"/> result.</returns>
    public static SecretReadResult Empty() => new(SecretReadStatus.Empty, null, null);

    /// <summary>
    /// Creates a result for a store that exists but could not be decrypted.
    /// </summary>
    /// <param name="reason">Why the read failed, phrased for a user.</param>
    /// <returns>An <see cref="SecretReadStatus.Unreadable"/> result.</returns>
    public static SecretReadResult Unreadable(string reason) =>
        new(SecretReadStatus.Unreadable, null, reason);
}

/// <summary>
/// A place to keep one blob of secret text at rest, encrypted where the platform offers it.
/// </summary>
/// <remarks>
/// <para>This is the ONE seam between Physalia and per-platform credential protection. It exists so
/// the Mac implementation is a new class rather than a second set of branches: everything above it
/// deals in a string payload and a <see cref="SecretReadResult"/>, and nothing above it names DPAPI,
/// the Keychain, or a file mode.</para>
/// <para>Implementations must be safe to construct on any OS — the per-platform choice belongs to
/// <see cref="SecretStores"/>, not to the caller — and must never throw from <see cref="Read"/>.
/// A store that cannot be read is recoverable (the user re-enters, or signs in as the right
/// account); an exception thrown up a request path is not.</para>
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Gets a short description of how this store protects its contents, for user-facing messages
    /// (e.g. "encrypted for your Windows account").
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets a value indicating whether the payload is encrypted at rest. False for the plaintext
    /// fallback, which relies on file permissions alone.
    /// </summary>
    bool IsEncrypted { get; }

    /// <summary>
    /// Reads and decrypts the stored payload. Never throws.
    /// </summary>
    /// <returns>The payload, or a status saying why there is none.</returns>
    SecretReadResult Read();

    /// <summary>
    /// Encrypts and writes the payload, replacing anything already stored.
    /// </summary>
    /// <param name="payload">The text to store.</param>
    void Write(string payload);

    /// <summary>
    /// Removes the stored payload if there is one. Never throws.
    /// </summary>
    void Delete();
}
