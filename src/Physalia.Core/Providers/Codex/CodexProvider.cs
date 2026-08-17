// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.Core.Models.Named;

namespace Physalia.Core.Providers.Codex;

/// <summary>
/// Provider that runs inference through the locally-installed OpenAI Codex CLI (<c>codex</c>).
/// Authentication is handled by the user's existing <c>codex login</c> session, so no API key is
/// required or used. Use with <see cref="CodexConfig"/>.
/// </summary>
/// <remarks>
/// Rather than cold-starting a fresh <c>codex</c> subprocess per call, this provider keeps one
/// <see cref="CodexSession"/> warm per conversation (keyed on <see cref="ModelConfig.SessionKey"/>,
/// which the LLM Call stamps with its instance GUID). The CLI cold start and the app-server thread
/// handshake are paid once on the seed turn; subsequent turns send only the new user message and
/// reuse the live process, which also lets the prompt cache cut latency. A session is dropped when
/// its conversation resets, its model, reasoning effort or system prompt changes, it errors, or the
/// LLM Call is removed (<see cref="EndSession"/>); an idle reaper kills abandoned sessions.
/// </remarks>
public sealed class CodexProvider : ILlmProvider
{
    private const string ContinuationInstruction =
        "Continue from the conversation above. Respond as the assistant.";

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<Guid, CodexSession> _sessions = new();
    private static readonly object _poolLock = new();

#pragma warning disable IDE0052 // Held to keep the reaper alive for the process lifetime.
    private static readonly Timer _reaper = new(_ => ReapIdleSessions(), null, IdleTimeout, IdleTimeout);
#pragma warning restore IDE0052

    static CodexProvider()
    {
        // Make sure no warm CLI process outlives the host (Rhino) process.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeAll();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="tools"/> is advertised to the model as the CLI's <c>dynamicTools</c>, and a
    /// call comes back on the final chunk for Physalia's Router to dispatch — the same contract the
    /// HTTP providers honour. The tools are declared when the thread opens, so changing the set
    /// starts a new session.
    /// </remarks>
    public async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
        Conversation conversation,
        SystemPrompt systemPrompt,
        ModelConfig config,
        IReadOnlyList<LlmToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (config is not CodexConfig codexConfig)
        {
            yield return Fail(LlmErrorKind.InvalidRequest, "CodexProvider requires a CodexConfig.");
            yield break;
        }

        // The session also accounts for the assistant turn the model is about to generate (the CLI
        // appends it to its thread; Conversation Log appends it to the Physalia conversation after
        // this call), so a completed turn brings the session up to conversation.Count + 1 messages.
        int consumedAfter = conversation.Count + 1;

        // No session key (a caller that did not opt into warm sessions): run a one-shot ephemeral
        // process that seeds the full history, then dispose it — same behaviour as a cold call.
        if (codexConfig.SessionKey is not Guid sessionKey)
        {
            var ephemeral = new CodexSession(codexConfig.ModelId, codexConfig.ReasoningEffort, systemPrompt.Text, tools);
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

        CodexSession session = ResolveSession(sessionKey, codexConfig, systemPrompt.Text, tools);

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
            // Reseed: a started session already holds thread context that cannot be rewound, so
            // replace it before seeding the full history. A never-started session
            // (ConsumedMessageCount 0, including one ResolveSession just recreated after a dead
            // process) is seeded as-is.
            if (session.ConsumedMessageCount > 0)
            {
                session = ReplaceSession(sessionKey, codexConfig, systemPrompt.Text, tools);
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
    /// <remarks>
    /// Asks the installed CLI which models the signed-in account may use, since that is
    /// plan-dependent and moves with each release. Falls back to <see cref="CodexConfig.KnownModels"/>
    /// only when the query fails outright, so a stale seed list never masks a working CLI.
    /// </remarks>
    public async Task<Result<IReadOnlyList<string>, LlmError>> GetAvailableModelsAsync(
        ModelConfig config,
        CancellationToken ct)
    {
        Result<IReadOnlyList<string>, LlmError> result = await CodexSession.ListModelsAsync(ct);

        if (result.IsOk(out IReadOnlyList<string>? models, out _) && models is { Count: > 0 })
        {
            return result;
        }

        return new Result<IReadOnlyList<string>, LlmError>.Ok(CodexConfig.KnownModels);
    }

    /// <summary>
    /// Gets a value indicating whether the Codex CLI (<c>codex</c>) is installed and resolvable on
    /// the system PATH. Used by the chat-window setup detection to decide whether Codex is an
    /// available provider. A presence check only — it does not verify that the user has
    /// authenticated with <c>codex login</c>.
    /// </summary>
    /// <returns>True when the CLI executable is found on PATH.</returns>
    public static bool IsCliAvailable() => CodexSession.IsCliAvailable();

    /// <summary>
    /// Kills and removes the warm session for the given key, if any. Called by the LLM Call when it
    /// is removed from the document so its CLI process does not leak.
    /// </summary>
    /// <param name="sessionKey">The session key (the LLM Call's instance GUID).</param>
    public static void EndSession(Guid sessionKey)
    {
        if (_sessions.TryRemove(sessionKey, out CodexSession? session))
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Streams one turn through a session, translating an exception thrown mid-turn (such as a
    /// cancellation) into a terminal error chunk so the iterator never throws into the caller.
    /// </summary>
    private static async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamTurnAsync(
        CodexSession session,
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
                    fatal = new LlmError(LlmErrorKind.Cancelled, "The Codex CLI call was cancelled.");
                    next = null;
                }
                catch (Exception ex)
                {
                    fatal = new LlmError(LlmErrorKind.Network, $"Codex CLI call failed: {ex.Message}");
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

    private static CodexSession ResolveSession(
        Guid sessionKey,
        CodexConfig config,
        string systemPrompt,
        IReadOnlyList<LlmToolDefinition>? tools)
    {
        lock (_poolLock)
        {
            if (_sessions.TryGetValue(sessionKey, out CodexSession? existing))
            {
                // A changed model, effort, system prompt or tool set cannot be applied to a running
                // thread — all four are fixed when the thread is opened.
                if (existing.ModelId == config.ModelId
                    && existing.ReasoningEffort == config.ReasoningEffort
                    && existing.SystemPrompt == systemPrompt
                    && SameTools(existing.Tools, tools)
                    && (existing.IsAlive || existing.ConsumedMessageCount == 0))
                {
                    return existing;
                }

                existing.Dispose();
            }

            var session = new CodexSession(config.ModelId, config.ReasoningEffort, systemPrompt, tools);
            _sessions[sessionKey] = session;
            return session;
        }
    }

    private static CodexSession ReplaceSession(
        Guid sessionKey,
        CodexConfig config,
        string systemPrompt,
        IReadOnlyList<LlmToolDefinition>? tools)
    {
        lock (_poolLock)
        {
            if (_sessions.TryRemove(sessionKey, out CodexSession? old))
            {
                old.Dispose();
            }

            var session = new CodexSession(config.ModelId, config.ReasoningEffort, systemPrompt, tools);
            _sessions[sessionKey] = session;
            return session;
        }
    }

    // Value equality over the declared set. LlmToolDefinition is a record, so this compares names,
    // descriptions and schemas — a tool node whose description was edited is a different contract
    // and has to be re-declared on a fresh thread.
    private static bool SameTools(IReadOnlyList<LlmToolDefinition> existing, IReadOnlyList<LlmToolDefinition>? incoming)
    {
        int count = incoming?.Count ?? 0;
        if (existing.Count != count)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (!existing[i].Equals(incoming![i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ReapIdleSessions()
    {
        DateTime cutoff = DateTime.UtcNow - IdleTimeout;
        foreach (KeyValuePair<Guid, CodexSession> entry in _sessions)
        {
            if (entry.Value.LastUsedUtc < cutoff && _sessions.TryRemove(entry.Key, out CodexSession? session))
            {
                session.Dispose();
            }
        }
    }

    private static void DisposeAll()
    {
        foreach (Guid key in _sessions.Keys)
        {
            if (_sessions.TryRemove(key, out CodexSession? session))
            {
                session.Dispose();
            }
        }
    }

    private static IReadOnlyList<MessageContent> BuildSeedContent(Conversation conversation)
    {
        // A single-turn conversation seeds with its real content blocks (so images survive). A
        // multi-turn history is serialised inline into one user message, since a fresh thread has
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
