// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.Versioning;
using System.Text;

namespace Physalia.Core.Config.Secrets;

/// <summary>
/// An <see cref="ISecretStore"/> that keeps its payload in one file, DPAPI-encrypted for the
/// current Windows user.
/// </summary>
/// <remarks>
/// Only the signed-in Windows account that wrote the file can read it. That is the whole protection
/// and its limit: it stops another local account, a stray backup, a screenshot of a folder or a
/// support bundle from carrying a usable key, and it does not stop code running AS the user, which
/// can call <c>CryptUnprotectData</c> exactly as this class does.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="DpapiSecretStore"/> class.
    /// </summary>
    /// <param name="path">Absolute path of the file holding the encrypted payload.</param>
    public DpapiSecretStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A store path is required.", nameof(path));

        _path = path;
    }

    /// <inheritdoc/>
    public string Description => "encrypted for your Windows account";

    /// <inheritdoc/>
    public bool IsEncrypted => true;

    /// <inheritdoc/>
    public SecretReadResult Read()
    {
        if (!File.Exists(_path))
            return SecretReadResult.Empty();

        byte[] ciphertext;
        try
        {
            ciphertext = File.ReadAllBytes(_path);
        }
        catch (Exception ex)
        {
            return SecretReadResult.Unreadable($"The credential file could not be opened: {ex.Message}");
        }

        if (ciphertext.Length == 0)
            return SecretReadResult.Empty();

        try
        {
            return SecretReadResult.Ok(Encoding.UTF8.GetString(WindowsDataProtection.Unprotect(ciphertext)));
        }
        catch (Exception ex)
        {
            // The overwhelmingly likely cause is a file written by another Windows account or
            // carried over from another machine — DPAPI keys do not travel. Say so, because the
            // alternative reading ("nothing is set up") sends the user off to re-enter every key.
            return SecretReadResult.Unreadable(
                "Saved credentials were found but could not be decrypted — they belong to a different "
                + $"Windows account or machine. Sign in as that account, or re-enter them here. ({ex.Message})");
        }
    }

    /// <inheritdoc/>
    public void Write(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(_path, WindowsDataProtection.Protect(Encoding.UTF8.GetBytes(payload)));
    }

    /// <inheritdoc/>
    public void Delete()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception)
        {
            // Deleting is best-effort: a locked file is not worth failing a save over.
        }
    }
}
