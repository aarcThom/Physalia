// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Physalia.Core.Config.Secrets;

/// <summary>
/// An <see cref="ISecretStore"/> that writes its payload as plain text, protected by file
/// permissions alone (owner read/write, nothing for group or other).
/// </summary>
/// <remarks>
/// <para>The fallback for platforms with no encrypted store wired up yet. It is what every other MCP
/// client does on those platforms, and it is deliberately honest about it — <see cref="IsEncrypted"/>
/// is false, so the setup UI can say so rather than implying a protection that is not there.</para>
/// <para>On macOS this is a placeholder for <c>KeychainSecretStore</c>, not the intended end state.
/// See <see cref="SecretStores"/>.</para>
/// </remarks>
public sealed class FileSecretStore : ISecretStore
{
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSecretStore"/> class.
    /// </summary>
    /// <param name="path">Absolute path of the file holding the payload.</param>
    public FileSecretStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A store path is required.", nameof(path));

        _path = path;
    }

    /// <inheritdoc/>
    public string Description => "stored in a file only your user account can read";

    /// <inheritdoc/>
    public bool IsEncrypted => false;

    /// <inheritdoc/>
    public SecretReadResult Read()
    {
        if (!File.Exists(_path))
            return SecretReadResult.Empty();

        try
        {
            string text = File.ReadAllText(_path, Encoding.UTF8);
            return string.IsNullOrWhiteSpace(text) ? SecretReadResult.Empty() : SecretReadResult.Ok(text);
        }
        catch (Exception ex)
        {
            return SecretReadResult.Unreadable($"The credential file could not be read: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Write(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(_path, payload, Encoding.UTF8);
        RestrictToOwner(_path);
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
            // Best-effort, as in DpapiSecretStore.
        }
    }

    // chmod 600. Unix-only API, and a no-op elsewhere: on Windows this class is the fallback of a
    // fallback (DPAPI is always available there), so there is nothing to tighten.
    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // A filesystem that cannot express the mode (a network share, some FUSE mounts) is not
            // a reason to refuse to save.
        }
    }
}
