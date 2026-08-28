// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;
using Physalia.Core.Common;

namespace Physalia.Core.Mcp;

/// <summary>
/// Process-wide pool of live MCP connections, keyed by <see cref="McpServerDefinition.Identity"/>.
/// </summary>
/// <remarks>
/// Same lifecycle contract as the local-CLI providers: one warm process per distinct server, shared
/// by every node naming it; an idle reaper kills abandoned ones; <c>ProcessExit</c> kills them all.
/// Pooling on the launch details rather than on a component id is what lets two nodes pointing at
/// the same server share a process — and what forces a fresh one when a token or argument changes,
/// since a warm process cannot re-authenticate.
/// </remarks>
public static class McpConnections
{
    private static readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(10);

    private static readonly ConcurrentDictionary<string, McpSession> _sessions = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _starting = new(StringComparer.Ordinal);
    private static readonly Timer _reaper;

    static McpConnections()
    {
        _reaper = new Timer(_ => ReapIdleSessions(), null, _idleTimeout, _idleTimeout);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeAll();
    }

    /// <summary>
    /// Returns the live session for a server, starting and initializing one if necessary.
    /// </summary>
    /// <param name="definition">The server to reach.</param>
    /// <param name="bridgeExecutable">Path to the MCP bridge, used only for a remote definition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A connected session, or the reason it could not be reached.</returns>
    public static async Task<Result<McpSession, LlmError>> GetAsync(
        McpServerDefinition definition,
        string? bridgeExecutable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);

        string identity = definition.Identity;

        if (_sessions.TryGetValue(identity, out McpSession? existing))
        {
            if (existing.IsAlive)
            {
                return new Result<McpSession, LlmError>.Ok(existing);
            }

            // A server that died since the last call is replaced, not reported: the caller asked for
            // a connection, and the next tool round should not fail on yesterday's crash.
            Retire(identity);
        }

        // One starter per identity, so two nodes solving in the same pass launch one process rather
        // than racing to launch two and orphaning one of them.
        SemaphoreSlim gate = _starting.GetOrAdd(identity, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_sessions.TryGetValue(identity, out McpSession? raced) && raced.IsAlive)
            {
                return new Result<McpSession, LlmError>.Ok(raced);
            }

            Result<McpSession, LlmError> started =
                await McpSession.StartAsync(definition, bridgeExecutable, ct).ConfigureAwait(false);

            if (started.IsOk(out McpSession? session, out _))
            {
                _sessions[identity] = session;
            }

            return started;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Reports whether a server currently has a live connection, without starting one.
    /// </summary>
    /// <param name="definition">The server to check.</param>
    /// <returns>True when a running session exists.</returns>
    public static bool IsConnected(McpServerDefinition definition) =>
        definition is not null &&
        _sessions.TryGetValue(definition.Identity, out McpSession? session) &&
        session.IsAlive;

    /// <summary>
    /// Returns the live session for a server if one exists, without starting anything.
    /// </summary>
    /// <param name="definition">The server to look up.</param>
    /// <returns>The session, or null when the server has never been reached or has died.</returns>
    public static McpSession? Find(McpServerDefinition definition)
    {
        if (definition is null || !_sessions.TryGetValue(definition.Identity, out McpSession? session))
        {
            return null;
        }

        return session.IsAlive ? session : null;
    }

    /// <summary>
    /// Ends the connection to one server, killing its process.
    /// </summary>
    /// <param name="definition">The server whose session should end.</param>
    public static void End(McpServerDefinition definition)
    {
        if (definition is not null)
        {
            Retire(definition.Identity);
        }
    }

    /// <summary>
    /// Kills every pooled session. Called on process exit, and available for a hard reset.
    /// </summary>
    public static void DisposeAll()
    {
        foreach (string key in _sessions.Keys)
        {
            Retire(key);
        }
    }

    private static void Retire(string identity)
    {
        if (_sessions.TryRemove(identity, out McpSession? session))
        {
            session.Dispose();
        }
    }

    private static void ReapIdleSessions()
    {
        DateTime cutoff = DateTime.UtcNow - _idleTimeout;

        foreach (KeyValuePair<string, McpSession> entry in _sessions)
        {
            if (entry.Value.LastUsedUtc < cutoff)
            {
                Retire(entry.Key);
            }
        }
    }
}
