// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.Core.Models.Named;

namespace Physalia.Core.Providers.ClaudeCode;

/// <summary>
/// Provider that runs inference through the locally-installed Claude Code CLI (<c>claude</c>).
/// Authentication is handled by the user's existing <c>claude auth login</c> session, so no API
/// key is required or used. Use with <see cref="ClaudeCodeConfig"/>.
/// </summary>
/// <remarks>
/// Rather than cold-starting a fresh <c>claude</c> subprocess per call, this provider keeps one
/// <see cref="ClaudeCodeSession"/> warm per conversation (keyed on <see cref="ModelConfig.SessionKey"/>,
/// which the LLM Call stamps with its instance GUID). The harness cold start is paid once on the
/// seed turn; subsequent turns send only the new user message and reuse the live process, which
/// also lets the CLI's prompt cache cut latency. A session is dropped when its conversation resets,
/// its model or system prompt changes, it errors, or the LLM Call is removed
/// (<see cref="EndSession"/>); an idle reaper kills abandoned sessions.
/// </remarks>
public sealed class ClaudeCodeProvider : ILlmProvider
{
    private const string ContinuationInstruction =
        "Continue from the conversation above. Respond as the assistant.";

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<Guid, ClaudeCodeSession> _sessions = new();
    private static readonly object _poolLock = new();

#pragma warning disable IDE0052 // Held to keep the reaper alive for the process lifetime.
    private static readonly Timer _reaper = new(_ => ReapIdleSessions(), null, IdleTimeout, IdleTimeout);
#pragma warning restore IDE0052

    static ClaudeCodeProvider()
    {
        // Make sure no warm CLI process outlives the host (Rhino) process.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeAll();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="tools"/> is ignored: the CLI invocation does not advertise Physalia tool
    /// definitions to the model.
    /// </remarks>
    public async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
        Conversation conversation,
        string systemPrompt,
        ModelConfig config,
        IReadOnlyList<LlmToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (config is not ClaudeCodeConfig claudeConfig)
        {
            yield return Fail(LlmErrorKind.InvalidRequest, "ClaudeCodeProvider requires a ClaudeCodeConfig.");
            yield break;
        }

        // The session also accounts for the assistant turn the model is about to generate (the CLI
        // appends it internally; Conversation Log appends it to the Physalia conversation after this call),
        // so a completed turn brings the session up to conversation.Count + 1 messages.
        int consumedAfter = conversation.Count + 1;

        // No session key (a caller that did not opt into warm sessions): run a one-shot ephemeral
        // process that seeds the full history, then dispose it — same behaviour as a cold call.
        if (claudeConfig.SessionKey is not Guid sessionKey)
        {
            var ephemeral = new ClaudeCodeSession(claudeConfig.ModelId, systemPrompt);
            try
            {
                await foreach (var chunk in StreamTurnAsync(ephemeral, BuildSeedContent(conversation), consumedAfter, ct))
                {
                    yield return chunk;
                }
            }
            finally
            {
                ephemeral.Dispose();
            }

            yield break;
        }

        ClaudeCodeSession session = ResolveSession(sessionKey, claudeConfig.ModelId, systemPrompt);

        // Decide seed vs. delta. A conversation is append-only, so anything that is not a clean
        // one-user-message extension of what the session already absorbed forces a fresh seed.
        bool isDelta = session.ConsumedMessageCount > 0
            && conversation.Count == session.ConsumedMessageCount + 1
            && conversation.Messages[^1].Role == Role.User;

        IReadOnlyList<MessageContent> content;
        if (isDelta)
        {
            content = conversation.Messages[^1].Content;
        }
        else
        {
            // Reseed: a started session already holds context that cannot be rewound, so replace
            // it before seeding the full history. A never-started session (ConsumedMessageCount 0,
            // including one ResolveSession just recreated after a dead process) is seeded as-is.
            if (session.ConsumedMessageCount > 0)
            {
                session = ReplaceSession(sessionKey, claudeConfig.ModelId, systemPrompt);
            }

            content = BuildSeedContent(conversation);
        }

        bool keepWarm = true;
        await foreach (var chunk in StreamTurnAsync(session, content, consumedAfter, ct))
        {
            if (chunk is Result<LlmResponseChunk, LlmError>.Err)
            {
                keepWarm = false;
            }

            yield return chunk;
        }

        // A turn that errored or was cancelled leaves the session desynced — drop it so the next
        // call cold-starts and reseeds cleanly.
        if (!keepWarm)
        {
            EndSession(sessionKey);
        }
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<string>, LlmError>> GetAvailableModelsAsync(
        ModelConfig config,
        CancellationToken ct)
    {
        // The CLI resolves these aliases to the current release, so the list never goes stale.
        return Task.FromResult<Result<IReadOnlyList<string>, LlmError>>(
            new Result<IReadOnlyList<string>, LlmError>.Ok(ClaudeCodeConfig.KnownModels));
    }

    /// <summary>
    /// Gets a value indicating whether the Claude Code CLI (<c>claude</c>) is installed and
    /// resolvable on the system PATH. Used by the chat-window setup detection to decide whether
    /// Claude Code is an available provider. A presence check only — it does not verify that the
    /// user has authenticated with <c>claude auth login</c>.
    /// </summary>
    /// <returns>True when the CLI executable is found on PATH.</returns>
    public static bool IsCliAvailable() => ClaudeCodeSession.IsCliAvailable();

    /// <summary>
    /// Kills and removes the warm session for the given key, if any. Called by the LLM Call when
    /// it is removed from the document so its CLI process does not leak.
    /// </summary>
    /// <param name="sessionKey">The session key (the LLM Call's instance GUID).</param>
    public static void EndSession(Guid sessionKey)
    {
        if (_sessions.TryRemove(sessionKey, out ClaudeCodeSession? session))
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Streams one turn through a session, translating an exception thrown mid-turn (such as a
    /// cancellation) into a terminal error chunk so the iterator never throws into the caller.
    /// </summary>
    private static async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamTurnAsync(
        ClaudeCodeSession session,
        IReadOnlyList<MessageContent> content,
        int newConsumedCount,
        [EnumeratorCancellation] CancellationToken ct)
    {
        IAsyncEnumerator<Result<LlmResponseChunk, LlmError>> enumerator =
            session.SendTurnAsync(content, newConsumedCount, ct).GetAsyncEnumerator(ct);

        try
        {
            while (true)
            {
                Result<LlmResponseChunk, LlmError>? next;
                LlmError? fatal = null;

                try
                {
                    next = await enumerator.MoveNextAsync() ? enumerator.Current : null;
                }
                catch (OperationCanceledException)
                {
                    fatal = new LlmError(LlmErrorKind.Cancelled, "The Claude Code CLI call was cancelled.");
                    next = null;
                }
                catch (Exception ex)
                {
                    fatal = new LlmError(LlmErrorKind.Network, $"Claude Code CLI call failed: {ex.Message}");
                    next = null;
                }

                if (fatal is not null)
                {
                    yield return new Result<LlmResponseChunk, LlmError>.Err(fatal);
                    yield break;
                }

                if (next is null)
                {
                    yield break;
                }

                yield return next;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static ClaudeCodeSession ResolveSession(Guid sessionKey, string modelId, string systemPrompt)
    {
        lock (_poolLock)
        {
            if (_sessions.TryGetValue(sessionKey, out ClaudeCodeSession? existing))
            {
                // A changed model or system prompt cannot be applied to a running process.
                if (existing.ModelId == modelId
                    && existing.SystemPrompt == systemPrompt
                    && (existing.IsAlive || existing.ConsumedMessageCount == 0))
                {
                    return existing;
                }

                existing.Dispose();
            }

            var session = new ClaudeCodeSession(modelId, systemPrompt);
            _sessions[sessionKey] = session;
            return session;
        }
    }

    private static ClaudeCodeSession ReplaceSession(Guid sessionKey, string modelId, string systemPrompt)
    {
        lock (_poolLock)
        {
            if (_sessions.TryRemove(sessionKey, out ClaudeCodeSession? old))
            {
                old.Dispose();
            }

            var session = new ClaudeCodeSession(modelId, systemPrompt);
            _sessions[sessionKey] = session;
            return session;
        }
    }

    private static void ReapIdleSessions()
    {
        DateTime cutoff = DateTime.UtcNow - IdleTimeout;
        foreach (KeyValuePair<Guid, ClaudeCodeSession> entry in _sessions)
        {
            if (entry.Value.LastUsedUtc < cutoff && _sessions.TryRemove(entry.Key, out ClaudeCodeSession? session))
            {
                session.Dispose();
            }
        }
    }

    private static void DisposeAll()
    {
        foreach (Guid key in _sessions.Keys)
        {
            if (_sessions.TryRemove(key, out ClaudeCodeSession? session))
            {
                session.Dispose();
            }
        }
    }

    private static IReadOnlyList<MessageContent> BuildSeedContent(Conversation conversation)
    {
        // A single-turn conversation seeds with its real content blocks (so images survive). A
        // multi-turn history is serialised inline into one user message, since a fresh process has
        // no prior context to continue from.
        if (conversation.Count == 1)
        {
            return conversation.Messages[0].Content;
        }

        string history = ConversationHelpers.ToDisplayString(conversation);
        string seed = string.IsNullOrEmpty(history)
            ? ContinuationInstruction
            : $"{history}\n\n{ContinuationInstruction}";

        return new MessageContent[] { new TextContent(seed) };
    }

    private static Result<LlmResponseChunk, LlmError> Fail(LlmErrorKind kind, string message)
        => new Result<LlmResponseChunk, LlmError>.Err(new LlmError(kind, message));
}
