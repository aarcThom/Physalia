// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Config.Secrets;

/// <summary>
/// Chooses the right <see cref="ISecretStore"/> for the running platform, and resolves where
/// Physalia keeps per-user data.
/// </summary>
/// <remarks>
/// <para><b>This is the only place the platform decision is made.</b> Callers ask for a store by
/// name and get whatever the OS can offer; nothing above this class branches on the operating
/// system, which is what keeps the Mac work down to one new class plus one line here.</para>
/// <para><b>Adding macOS Keychain support:</b> implement <c>KeychainSecretStore : ISecretStore</c>
/// (Security.framework <c>SecItemAdd</c> / <c>SecItemCopyMatching</c> against a generic-password
/// item keyed on the store name), then add the <c>OperatingSystem.IsMacOS()</c> arm to
/// <see cref="For"/>. Nothing else changes — <see cref="SecretReadStatus.Unreadable"/> already
/// covers a Keychain the user declined to unlock, which is the Mac analogue of a DPAPI blob written
/// by another account.</para>
/// </remarks>
public static class SecretStores
{
    /// <summary>
    /// Returns the best available store for a named secret.
    /// </summary>
    /// <param name="name">
    /// A short file-safe name identifying what is being stored, e.g. "credentials". Becomes the
    /// file name on the file-backed implementations.
    /// </param>
    /// <returns>An encrypted store where the platform provides one, else the file fallback.</returns>
    public static ISecretStore For(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A store name is required.", nameof(name));

        string path = Path.Combine(DataFolder(), name + ".dat");

        // Windows: DPAPI, always present, no configuration.
        if (OperatingSystem.IsWindows())
            return new DpapiSecretStore(path);

        // macOS: Keychain goes here — see the class remarks. Until then the file fallback, which is
        // what other MCP clients ship on this platform.
        return new FileSecretStore(path);
    }

    /// <summary>
    /// Gets Physalia's per-user data folder, creating it if needed.
    /// </summary>
    /// <remarks>
    /// <c>%LOCALAPPDATA%/Physalia</c> on Windows, <c>~/.local/share/Physalia</c> elsewhere — the
    /// same root the MCP bridge's OAuth token cache uses. Deliberately NOT the plug-in's
    /// <c>Files/</c> folder: that is declared user-alterable content, it sits in the install
    /// directory where a plug-in update can overwrite it, and per-user-per-machine secrets have no
    /// business in a location shared by every account on the box.
    /// </remarks>
    /// <returns>The absolute path to the data folder.</returns>
    public static string DataFolder()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        string folder = Path.Combine(root, "Physalia");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
