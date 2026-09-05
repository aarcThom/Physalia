// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using Physalia.Core.Config.Secrets;

namespace Physalia.McpBridge;

/// <summary>
/// An <see cref="ITokenCache"/> that keeps a server's OAuth tokens on disk, per Windows/OS user, so
/// a sign-in survives the bridge process.
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> Without a cache the SDK keeps tokens with the transport, so
/// they die with the bridge — and the bridge is short-lived by design: <c>McpConnections</c> reaps an
/// idle session after ten minutes and every Rhino restart kills the pool. The user would face a
/// browser sign-in on essentially every cold start, and signing in from the setup page would be
/// theatre, since the credential would be gone before a node was ever placed.</para>
/// <para><b>What makes a cold start work is the ClientId, not just the refresh token.</b>
/// <see cref="TokenContainer"/> carries the id the dynamic client registration produced, and the SDK
/// restores it from the cache — so a persisted refresh token can be redeemed without re-registering
/// and without prompting. Dropping that field to "just store the tokens" would silently reintroduce
/// the prompt.</para>
/// <para><b>At rest it is DPAPI-encrypted on Windows</b> (<see cref="DataProtectionScope.CurrentUser"/>),
/// so only the signed-in Windows account can read it. DPAPI is Windows-only; elsewhere the file is
/// written plaintext with owner-only permissions, which is what the platform offers and what other
/// MCP clients do everywhere.</para>
/// <para>Every failure path returns "no cached token" rather than throwing. A cache that cannot be
/// read is exactly as recoverable as no cache — the user signs in again — whereas an exception here
/// would take down a connection that was otherwise fine.</para>
/// </remarks>
internal sealed class FileTokenCache : ITokenCache
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // GetTokensAsync is called on the REQUEST HOT PATH — the SDK's own docs say "invoked for every
    // request" — so the disk is read once and the result kept here. Without this every JSON-RPC
    // message would cost a file read and a DPAPI decrypt.
    private TokenContainer? _cached;
    private bool _loaded;

    /// <summary>
    /// Creates a cache for one server endpoint.
    /// </summary>
    /// <param name="endpoint">The server's URL, which identifies whose tokens these are.</param>
    /// <param name="scope">The requested OAuth scope, or null. Part of the key: a token issued for
    /// one scope must not be handed to a connection asking for another.</param>
    public FileTokenCache(Uri endpoint, string? scope)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _path = Path.Combine(CacheDirectory(), $"{KeyFor(endpoint, scope)}.tok");
    }

    /// <summary>
    /// Gets the directory tokens are kept in, creating it if necessary.
    /// </summary>
    /// <returns>The absolute path to the token directory.</returns>
    public static string CacheDirectory()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        // A bare fallback for a platform that reports no local-app-data folder at all; better a
        // token beside the executable than a crash on startup.
        if (string.IsNullOrEmpty(root))
        {
            root = AppContext.BaseDirectory;
        }

        string directory = Path.Combine(root, "Physalia", "mcp-auth");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <inheritdoc/>
    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cached = tokens;
            _loaded = true;

            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(tokens, McpJsonUtilities.DefaultOptions);
            await File.WriteAllBytesAsync(_path, Protect(plain), cancellationToken).ConfigureAwait(false);
            RestrictToOwner(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // The in-memory copy above still serves this process, so a read-only disk costs the user
            // a sign-in next time rather than the session they are in.
            await Console.Error.WriteLineAsync($"bridge: could not cache tokens ({ex.Message})").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return _cached;
            }

            _loaded = true;
            _cached = null;

            if (!File.Exists(_path))
            {
                return null;
            }

            byte[] stored = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            _cached = JsonSerializer.Deserialize<TokenContainer>(Unprotect(stored), McpJsonUtilities.DefaultOptions);

            if (_cached is not null)
            {
                await Console.Error.WriteLineAsync("bridge: reusing a cached sign-in").ConfigureAwait(false);
            }

            return _cached;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Unreadable for any reason — corrupt, written by another user account, a changed
            // machine key — is the same situation as never having signed in.
            await Console.Error.WriteLineAsync($"bridge: ignoring an unreadable token cache ({ex.Message})").ConfigureAwait(false);
            _cached = null;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Endpoint + scope, hashed: the file name must not leak which services the user has connected to
    // by sitting in a directory listing, and a URL is not a legal file name anyway.
    private static string KeyFor(Uri endpoint, string? scope)
    {
        string key = $"{endpoint.AbsoluteUri}{scope ?? string.Empty}";
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(digest).ToLowerInvariant()[..32];
    }

    // Shares Physalia.Core's DPAPI wrapper by LINKED SOURCE (see the .csproj) — the bridge is a leaf
    // executable with no project reference to Core, by design. The byte format is unchanged from the
    // ProtectedData calls this replaced (no entropy, current-user scope), so token caches written by
    // earlier builds still open.
    private static byte[] Protect(byte[] plain) =>
        OperatingSystem.IsWindows() ? WindowsDataProtection.Protect(plain) : plain;

    private static byte[] Unprotect(byte[] stored) =>
        OperatingSystem.IsWindows() ? WindowsDataProtection.Unprotect(stored) : stored;

    // Windows is covered by DPAPI above; elsewhere the bytes are plaintext, so the file mode is the
    // only thing standing between a refresh token and every other account on the machine.
    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort: a filesystem that cannot express the mode is not a reason to refuse to
            // cache, and the failure is already visible to anyone auditing the directory.
        }
    }
}
