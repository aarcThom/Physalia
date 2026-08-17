// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Providers.Codex;

/// <summary>
/// A single long-lived <c>codex app-server</c> process held open over stdio, so the CLI cold start
/// and the thread handshake are paid once and amortised across every turn of one conversation.
/// The transport is line-delimited JSON-RPC 2.0: the session performs the <c>initialize</c> /
/// <c>initialized</c> handshake, opens one thread with <c>thread/start</c>, and then each call to
/// <see cref="SendTurnAsync"/> issues one <c>turn/start</c> and streams the reply back until
/// <c>turn/completed</c>. The server keeps conversation context per thread, so callers send only
/// the new turn after the seed. Owned and pooled by <see cref="CodexProvider"/>.
/// </summary>
/// <remarks>
/// The system prompt rides on <c>baseInstructions</c>, which REPLACES the agent's own base prompt —
/// it is fixed at thread start, so a changed system prompt requires a fresh session, as does a
/// changed model (fixed by <c>thread/start</c>). The thread is opened <c>ephemeral</c> (nothing
/// written to the session store), read-only sandboxed, and with approvals set to <c>never</c>, and
/// the heavier agentic features are switched off at launch, so the process behaves as a plain text
/// generator rather than running work of its own. One turn runs at a time, guarded by a semaphore.
/// </remarks>
internal sealed class CodexSession : IDisposable
{
    /// <summary>
    /// The lowest Codex CLI version this session has been verified against. The v2 app-server
    /// methods it speaks (<c>thread/start</c>, <c>turn/start</c>) are marked experimental by the
    /// CLI's own docs, so an older install may answer differently or not at all. The session still
    /// TRIES an older CLI — the version only shapes the error text when startup actually fails, so
    /// a working older install is never turned away. Lower this once an earlier version is
    /// genuinely verified; it is set to the version it was confirmed on, not to a guess.
    /// </summary>
    internal static readonly Version MinVerifiedVersion = new(0, 142, 3);

    // How long the initialize/thread-start handshake may take before the session gives up. Generous:
    // the first launch of the CLI can pay a cold start, but it must not hang a Grasshopper solve
    // forever if the process comes up wedged.
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(90);

    // A dedicated empty directory the app-server runs in, so the agent's workspace / AGENTS.md
    // auto-discovery finds nothing to load. Created once and reused across every session's process.
    private static readonly Lazy<string> _isolatedWorkingDir = new(CreateIsolatedWorkingDirectory);

    // The installed CLI's version (from `codex --version`), detected once per process and cached.
    // Null when detection failed for any reason (missing binary, parse failure, timeout).
    private static readonly Lazy<Version?> _cliVersion = new(DetectCliVersion);

    // The app-server emits UTF-8 NDJSON and reads UTF-8 on stdin; pin both pipes so multibyte
    // characters in the prompt or the generated JSON survive. No BOM — a BOM would corrupt the
    // first stdin line.
    private static readonly Encoding _pipeEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private readonly List<string> _tempImageFiles = new();
    private Process? _process;
    private Task? _stderrPump;
    private volatile string _stderr = string.Empty;
    private string? _threadId;
    private long _nextRequestId;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodexSession"/> class. The process is not
    /// started until the first <see cref="SendTurnAsync"/> call.
    /// </summary>
    /// <param name="modelId">The Codex model the CLI should use; empty for the CLI's own default.</param>
    /// <param name="reasoningEffort">The reasoning effort to request per turn, or null for the model default.</param>
    /// <param name="systemPrompt">The system prompt, fixed for the life of the thread.</param>
    public CodexSession(string modelId, string? reasoningEffort, string systemPrompt)
    {
        ModelId = modelId;
        ReasoningEffort = reasoningEffort;
        SystemPrompt = systemPrompt;
    }

    /// <summary>
    /// Gets the model ID this session's thread was started with.
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// Gets the reasoning effort this session requests on every turn, or null for the model default.
    /// </summary>
    public string? ReasoningEffort { get; }

    /// <summary>
    /// Gets the system prompt this session's thread was started with.
    /// </summary>
    public string SystemPrompt { get; }

    /// <summary>
    /// Gets the number of <see cref="Conversation"/> messages this session has already absorbed.
    /// Zero until the first (seed) turn completes; the provider uses it to decide seed vs. delta.
    /// </summary>
    public int ConsumedMessageCount { get; private set; }

    /// <summary>
    /// Gets the UTC time of the most recent turn, used by the provider's idle reaper.
    /// </summary>
    public DateTime LastUsedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets a value indicating whether the underlying process is started and still running.
    /// </summary>
    public bool IsAlive => _process is { HasExited: false };

    /// <summary>
    /// Sends one user turn to the live process and streams the reply chunks until the server's
    /// <c>turn/completed</c> notification. Text deltas are yielded as they arrive, reasoning
    /// summaries as inline <c>&lt;think&gt;</c> blocks; the final chunk carries token usage.
    /// On success the session stays warm for the next turn.
    /// </summary>
    /// <param name="content">The content blocks of the user turn (text and/or images).</param>
    /// <param name="newConsumedCount">
    /// The <see cref="Conversation.Count"/> this turn brings the session up to, recorded into
    /// <see cref="ConsumedMessageCount"/> once the turn completes successfully.
    /// </param>
    /// <param name="ct">Cancellation token; cancelling mid-turn desyncs the session.</param>
    /// <returns>An async sequence of result chunks for this turn.</returns>
    public async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> SendTurnAsync(
        IReadOnlyList<MessageContent> content,
        int newConsumedCount,
        [EnumeratorCancellation] CancellationToken ct)
    {
        LastUsedUtc = DateTime.UtcNow;
        await _turnLock.WaitAsync(ct);
        try
        {
            await EnsureStartedAsync(ct);

            long turnRequestId = await SendRequestAsync(
                "turn/start",
                new
                {
                    threadId = _threadId,
                    input = BuildUserInput(content),

                    // The load-bearing half of visible reasoning: without an explicit summary the
                    // server streams a reasoning item with no text at all (measured on 0.142.3 —
                    // an empty `reasoning` item, no summaryTextDelta lines). "auto" is what the
                    // interactive TUI asks for.
                    summary = "auto",
                    effort = string.IsNullOrWhiteSpace(ReasoningEffort) ? null : ReasoningEffort,
                },
                ct);

            bool emittedText = false;
            string? finalText = null;
            LlmUsage? usage = null;

            // Reasoning summaries are re-emitted inline as <think>…</think>, exactly as the
            // Anthropic and Claude Code providers do, so the chat UI renders them and ThinkingTags
            // strips them from resent history. The tag opens lazily on the first non-empty delta,
            // so a turn that summarises nothing emits no tags at all.
            bool thinkingTagOpen = false;

            while (true)
            {
                string? raw = await ReadLineAsync(ct);
                if (raw is null)
                {
                    yield return Fail(
                        LlmErrorKind.Network,
                        DescribeFailure($"The Codex CLI ended unexpectedly. {Summarise(_stderr)}"));
                    yield break;
                }

                ParsedLine parsed = ParseLine(raw);
                switch (parsed.Kind)
                {
                    case LineKind.ServerRequest:
                        // Nothing here implements the agent's side-channel requests (approvals,
                        // tool calls, elicitations). Answering with a JSON-RPC error declines them
                        // and, more importantly, unblocks the server — an unanswered request would
                        // stall the turn forever.
                        await SendServerRequestRefusalAsync(parsed.RequestId, ct);
                        break;

                    case LineKind.TextDelta:
                    {
                        string? delta = parsed.Text;
                        if (!string.IsNullOrEmpty(delta))
                        {
                            // A summary that never got its own item/completed is closed here: the
                            // answer has started, so the thinking block is over.
                            if (thinkingTagOpen)
                            {
                                thinkingTagOpen = false;
                                delta = ThinkingTags.CloseAndSeparate + delta;
                            }

                            emittedText = true;
                            yield return Ok(new LlmResponseChunk(delta, IsLast: false, Usage: null));
                        }

                        break;
                    }

                    case LineKind.ThinkingDelta:
                    {
                        string thinking = parsed.Text ?? string.Empty;
                        if (thinking.Length > 0)
                        {
                            // Deliberately does NOT set emittedText: a turn that streamed only a
                            // reasoning summary still has no answer, so the final-text fallback
                            // below must still fire.
                            string prefix = thinkingTagOpen ? string.Empty : ThinkingTags.Open;
                            thinkingTagOpen = true;
                            yield return Ok(new LlmResponseChunk(prefix + thinking, IsLast: false, Usage: null));
                        }

                        break;
                    }

                    case LineKind.ThinkingPart:
                        // A new summary part is a fresh paragraph, not a fresh block.
                        if (thinkingTagOpen)
                        {
                            yield return Ok(new LlmResponseChunk("\n\n", IsLast: false, Usage: null));
                        }

                        break;

                    case LineKind.ThinkingStop:
                        if (thinkingTagOpen)
                        {
                            thinkingTagOpen = false;
                            yield return Ok(new LlmResponseChunk(ThinkingTags.CloseAndSeparate, IsLast: false, Usage: null));
                        }

                        break;

                    case LineKind.FinalText:
                        finalText = parsed.Text;
                        break;

                    case LineKind.Usage:
                        usage = parsed.Usage;
                        break;

                    case LineKind.Error:
                        // A retryable error is the server telling us it is having another go —
                        // the turn is still live, so it is not surfaced as a failure.
                        if (parsed.WillRetry)
                        {
                            break;
                        }

                        yield return Fail(parsed.ErrorKind, parsed.Text ?? "The Codex CLI reported an error.");
                        yield break;

                    case LineKind.RequestFailed:
                        if (parsed.RequestId == turnRequestId)
                        {
                            yield return Fail(parsed.ErrorKind, parsed.Text ?? "The Codex CLI rejected the turn.");
                            yield break;
                        }

                        break;

                    case LineKind.TurnFailed:
                        yield return Fail(parsed.ErrorKind, parsed.Text ?? "The Codex CLI turn failed.");
                        yield break;

                    case LineKind.TurnCompleted:
                    {
                        ConsumedMessageCount = newConsumedCount;
                        LastUsedUtc = DateTime.UtcNow;

                        // If no streamed text arrived (a very short reply, or one delivered whole),
                        // fall back to the completed message's full text so the turn is never empty.
                        string? closing = emittedText ? null : finalText;

                        // A summary block still open here never got its item/completed — close it
                        // on the final chunk rather than leaving the UI (and the history stripper)
                        // with an unterminated tag.
                        if (thinkingTagOpen)
                        {
                            thinkingTagOpen = false;
                            closing = string.IsNullOrEmpty(closing)
                                ? ThinkingTags.Close
                                : ThinkingTags.CloseAndSeparate + closing;
                        }

                        yield return Ok(new LlmResponseChunk(closing, IsLast: true, usage, ToolCalls: null, parsed.Text));
                        yield break;
                    }

                    default:
                        break;
                }
            }
        }
        finally
        {
            _turnLock.Release();
        }
    }

    /// <summary>
    /// Asks a throwaway app-server process which models the signed-in account may use. Live rather
    /// than hard-coded because the answer is plan-dependent and moves with each CLI release.
    /// </summary>
    /// <param name="ct">Cancellation token bounding the query.</param>
    /// <returns>The model IDs, or an error.</returns>
    public static async Task<Result<IReadOnlyList<string>, LlmError>> ListModelsAsync(CancellationToken ct)
    {
        var session = new CodexSession(string.Empty, null, string.Empty);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(HandshakeTimeout);
            CancellationToken token = timeout.Token;

            session.StartProcess();
            await session.HandshakeAsync(token);

            long id = await session.SendRequestAsync("model/list", new { includeHidden = false, limit = 100 }, token);
            JsonElement result = await session.AwaitResponseAsync(id, "model/list", token);

            var models = new List<string>();
            if (result.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in data.EnumerateArray())
                {
                    if (entry.TryGetProperty("id", out JsonElement idEl) && idEl.GetString() is string modelId
                        && !string.IsNullOrWhiteSpace(modelId) && !models.Contains(modelId))
                    {
                        models.Add(modelId);
                    }
                }
            }

            return new Result<IReadOnlyList<string>, LlmError>.Ok(models);
        }
        catch (OperationCanceledException)
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(
                new LlmError(LlmErrorKind.Timeout, "Timed out asking the Codex CLI for its model list."));
        }
        catch (Exception ex)
        {
            return new Result<IReadOnlyList<string>, LlmError>.Err(
                new LlmError(LlmErrorKind.Network, $"Could not read the Codex CLI model list: {ex.Message}"));
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the <c>codex</c> CLI executable can be found on the system
    /// PATH. A presence check only — it does not verify the user is authenticated.
    /// </summary>
    /// <returns>True when the CLI executable is resolvable on PATH.</returns>
    internal static bool IsCliAvailable() => TryResolveExecutable(out _);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    // Closing stdin lets the app-server exit cleanly; kill the tree if it lingers.
                    // The tree matters here: the npm shim launches the real binary as a child, and
                    // the agent may itself have spawned MCP server processes.
                    try
                    {
                        _process.StandardInput.Close();
                    }
                    catch
                    {
                        // Best effort.
                    }

                    if (!_process.WaitForExit(500))
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
                // Best effort — the process may already have exited.
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        foreach (string file in _tempImageFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Best effort — temp file cleanup.
            }
        }

        _tempImageFiles.Clear();
        _turnLock.Dispose();
    }

    private async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (_process is { HasExited: false } && _threadId is not null)
        {
            return;
        }

        if (_process is not null)
        {
            throw new InvalidOperationException(DescribeFailure("The Codex CLI session process has exited."));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HandshakeTimeout);
        CancellationToken token = timeout.Token;

        StartProcess();
        await HandshakeAsync(token);

        var threadParams = new Dictionary<string, object?>
        {
            // Replaces the agent's own base prompt, so the process answers as Physalia's model
            // rather than as a coding agent.
            ["baseInstructions"] = string.IsNullOrEmpty(SystemPrompt) ? null : SystemPrompt,
            ["model"] = string.IsNullOrWhiteSpace(ModelId) ? null : ModelId,
            ["cwd"] = _isolatedWorkingDir.Value,

            // Nothing about this thread belongs in the user's Codex session history.
            ["ephemeral"] = true,

            // Belt and braces around the feature switches in BuildStartInfo: if the model does
            // reach for a tool, it can neither write nor ask a human for permission to.
            ["sandbox"] = "read-only",
            ["approvalPolicy"] = "never",
        };

        long id = await SendRequestAsync("thread/start", threadParams, token);
        JsonElement result = await AwaitResponseAsync(id, "thread/start", token);

        _threadId = result.TryGetProperty("thread", out JsonElement thread)
            && thread.TryGetProperty("id", out JsonElement threadId)
                ? threadId.GetString()
                : null;

        if (string.IsNullOrEmpty(_threadId))
        {
            throw new InvalidOperationException(
                DescribeFailure("The Codex CLI app-server did not return a thread id."));
        }
    }

    private async Task HandshakeAsync(CancellationToken ct)
    {
        long id = await SendRequestAsync(
            "initialize",
            new { clientInfo = new { name = "physalia", version = "1.0.0", title = "Physalia" } },
            ct);

        await AwaitResponseAsync(id, "initialize", ct);
        await SendNotificationAsync("initialized", ct);
    }

    private void StartProcess()
    {
        var process = new Process { StartInfo = BuildStartInfo() };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(DescribeFailure("Failed to start the Codex CLI process."));
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                DescribeFailure($"Could not launch the Codex CLI: {ex.Message}. Install Codex and run `codex login`."),
                ex);
        }

        _process = process;
        _stderr = string.Empty;

        // Drain stderr on a background task so a full pipe buffer can never deadlock the turn.
        _stderrPump = Task.Run(async () =>
        {
            try
            {
                _stderr = await process.StandardError.ReadToEndAsync();
            }
            catch
            {
                // Best effort — the process may exit while reading.
            }
        });
    }

    private static ProcessStartInfo BuildStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutable(),
            WorkingDirectory = _isolatedWorkingDir.Value,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = _pipeEncoding,
            StandardOutputEncoding = _pipeEncoding,
            StandardErrorEncoding = _pipeEncoding,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // The stdio transport IS the protocol: line-delimited JSON-RPC 2.0 both ways, one process
        // held open for the life of the conversation.
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");

        // Switch off the agentic surface. These are not safety measures — the sandbox and the
        // approval policy on the thread cover that — they are there so the process behaves as a
        // plain text generator and does not pay for tool definitions it must never call. Measured
        // on 0.142.3: ~2.4k input tokens per turn saved, and one fewer MCP subprocess launched.
        // Unknown feature names resolve to a harmless `features.<name>=false` override, so an
        // older or newer CLI that has never heard of one of these is not disturbed by it.
        foreach (string feature in DisabledFeatures)
        {
            startInfo.ArgumentList.Add("--disable");
            startInfo.ArgumentList.Add(feature);
        }

        foreach (string over in ConfigOverrides)
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(over);
        }

        return startInfo;
    }

    // Agentic features switched off at launch. `plugins`/`apps` also stop the bundled MCP servers
    // they bring with them. A server the user configured under [mcp_servers] in their own
    // config.toml still loads: overriding that table from the command line MERGES rather than
    // replaces (tried both `-c mcp_servers={}` and the thread-level `config` object, neither drops
    // it), and the alternative — pointing CODEX_HOME somewhere empty — would take the user's
    // credentials with it, since that is where `codex login` writes them.
    private static readonly string[] DisabledFeatures =
    {
        "plugins",
        "apps",
        "browser_use",
        "browser_use_external",
        "browser_use_full_cdp_access",
        "computer_use",
        "in_app_browser",
        "image_generation",
        "multi_agent",
        "goals",
        "memories",
        "hooks",
        "tool_suggest",
    };

    // TOML config overrides, parsed by the CLI exactly as if they were in config.toml. `notify` is
    // the sharp one: a user with a notify hook configured would otherwise have it spawn a process
    // on every single turn of every inference.
    private static readonly string[] ConfigOverrides =
    {
        "tools.web_search=false",
        "tools.view_image=false",
        "notify=[]",
        "analytics.enabled=false",
    };

    private object[] BuildUserInput(IReadOnlyList<MessageContent> content)
    {
        var items = new List<object>(content.Count);
        foreach (MessageContent block in content)
        {
            switch (block)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    items.Add(new { type = "text", text = text.Text });
                    break;

                case ImageContent { Source: UrlImage url }:
                    items.Add(new { type = "image", url = url.Url });
                    break;

                case ImageContent { Source: InlineImage inline }:
                {
                    // The protocol takes images by path or URL, not as inline bytes, so an inline
                    // image is spooled to a temp file for the life of the session.
                    string? path = WriteTempImage(inline);
                    if (path is not null)
                    {
                        items.Add(new { type = "localImage", path });
                    }

                    break;
                }

                default:
                    break;
            }
        }

        // Guard against an all-unmappable turn producing an empty input array, which the server
        // rejects.
        if (items.Count == 0)
        {
            items.Add(new { type = "text", text = string.Empty });
        }

        return items.ToArray();
    }

    private string? WriteTempImage(InlineImage image)
    {
        try
        {
            string extension = image.MimeType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".png",
            };

            string path = Path.Combine(Path.GetTempPath(), $"physalia-codex-{Guid.NewGuid():N}{extension}");
            File.WriteAllBytes(path, image.Data);
            _tempImageFiles.Add(path);
            return path;
        }
        catch
        {
            // An image that cannot be spooled is dropped rather than failing the whole turn.
            return null;
        }
    }

    private async Task<long> SendRequestAsync(string method, object? parameters, CancellationToken ct)
    {
        long id = Interlocked.Increment(ref _nextRequestId);
        await WriteMessageAsync(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters,
            },
            ct);

        return id;
    }

    private Task SendNotificationAsync(string method, CancellationToken ct)
        => WriteMessageAsync(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
            },
            ct);

    private Task SendServerRequestRefusalAsync(long id, CancellationToken ct)
        => WriteMessageAsync(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,

                // -32601 is JSON-RPC's "method not found": Physalia's client implements none of
                // the agent's callbacks.
                ["error"] = new { code = -32601, message = "Physalia does not implement agent side-channel requests." },
            },
            ct);

    private async Task WriteMessageAsync(object message, CancellationToken ct)
    {
        Process process = _process ?? throw new InvalidOperationException("The Codex CLI session is not started.");

        // Default (non-indented) serialisation keeps this to a single NDJSON line; any newlines
        // inside text are escaped, so the line framing stays intact.
        string line = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
        await process.StandardInput.FlushAsync();
    }

    private async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        Process process = _process ?? throw new InvalidOperationException("The Codex CLI session is not started.");
        return await process.StandardOutput.ReadLineAsync(ct);
    }

    /// <summary>
    /// Reads until the response to the given request id arrives, answering any server request that
    /// turns up on the way and ignoring notifications. Returns the response's <c>result</c>.
    /// </summary>
    private async Task<JsonElement> AwaitResponseAsync(long requestId, string method, CancellationToken ct)
    {
        while (true)
        {
            string? raw = await ReadLineAsync(ct);
            if (raw is null)
            {
                throw new InvalidOperationException(
                    DescribeFailure($"The Codex CLI ended before answering `{method}`. {Summarise(_stderr)}"));
            }

            if (string.IsNullOrWhiteSpace(raw) || raw[0] != '{')
            {
                continue;
            }

            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            if (!root.TryGetProperty("id", out JsonElement idEl) || !idEl.TryGetInt64(out long id))
            {
                continue;
            }

            // A message carrying both an id and a method is a REQUEST from the server, not the
            // response we are waiting for.
            if (root.TryGetProperty("method", out _))
            {
                await SendServerRequestRefusalAsync(id, ct);
                continue;
            }

            if (id != requestId)
            {
                continue;
            }

            if (root.TryGetProperty("error", out JsonElement error))
            {
                throw new InvalidOperationException(
                    DescribeFailure($"The Codex CLI rejected `{method}`: {DescribeRpcError(error)}"));
            }

            return root.TryGetProperty("result", out JsonElement result) ? result : default;
        }
    }

    private static ParsedLine ParseLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw[0] != '{')
        {
            return ParsedLine.Other;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;

            bool hasId = root.TryGetProperty("id", out JsonElement idEl) && idEl.TryGetInt64(out _);
            if (!root.TryGetProperty("method", out JsonElement methodEl))
            {
                // No method: a response to one of our requests. Only a failure is interesting —
                // the successful `turn/start` reply just echoes the turn we already know about.
                if (hasId && root.TryGetProperty("error", out JsonElement rpcError))
                {
                    return new ParsedLine(
                        LineKind.RequestFailed,
                        DescribeRpcError(rpcError),
                        RequestId: idEl.GetInt64());
                }

                return ParsedLine.Other;
            }

            // A method AND an id is a request from the server that must be answered.
            if (hasId)
            {
                return new ParsedLine(LineKind.ServerRequest, RequestId: idEl.GetInt64());
            }

            string method = methodEl.GetString() ?? string.Empty;
            root.TryGetProperty("params", out JsonElement p);

            switch (method)
            {
                case "item/agentMessage/delta":
                    return new ParsedLine(LineKind.TextDelta, ReadString(p, "delta"));

                // Both reasoning streams land in the same inline <think> block: `summaryTextDelta`
                // is the summarised form the server sends when a summary is asked for, `textDelta`
                // the raw form some models emit instead.
                case "item/reasoning/summaryTextDelta":
                case "item/reasoning/textDelta":
                    return new ParsedLine(LineKind.ThinkingDelta, ReadString(p, "delta"));

                case "item/reasoning/summaryPartAdded":
                    return new ParsedLine(LineKind.ThinkingPart);

                case "item/started":
                    // The answer starting is the reliable end of the reasoning block: a reasoning
                    // item's own item/completed can arrive after the first answer delta.
                    return ItemType(p) == "agentMessage" ? new ParsedLine(LineKind.ThinkingStop) : ParsedLine.Other;

                case "item/completed":
                    return ItemType(p) switch
                    {
                        "agentMessage" => new ParsedLine(LineKind.FinalText, ReadItemString(p, "text")),
                        "reasoning" => new ParsedLine(LineKind.ThinkingStop),
                        _ => ParsedLine.Other,
                    };

                case "thread/tokenUsage/updated":
                    return new ParsedLine(LineKind.Usage, Usage: ParseUsage(p));

                case "error":
                {
                    p.TryGetProperty("error", out JsonElement err);
                    bool willRetry = p.TryGetProperty("willRetry", out JsonElement retryEl)
                        && retryEl.ValueKind == JsonValueKind.True;
                    (LlmErrorKind kind, string message) =
                        DescribeAgentError(err, "The Codex CLI reported an error.");
                    return new ParsedLine(LineKind.Error, message, ErrorKind: kind, WillRetry: willRetry);
                }

                case "turn/completed":
                {
                    p.TryGetProperty("turn", out JsonElement turn);
                    string status = ReadString(turn, "status") ?? "completed";
                    if (status == "failed")
                    {
                        turn.TryGetProperty("error", out JsonElement turnError);
                        (LlmErrorKind kind, string message) =
                            DescribeAgentError(turnError, "The Codex CLI turn failed.");
                        return new ParsedLine(LineKind.TurnFailed, message, ErrorKind: kind);
                    }

                    // The status doubles as the stop reason on the final chunk.
                    return new ParsedLine(LineKind.TurnCompleted, status);
                }

                default:
                    return ParsedLine.Other;
            }
        }
        catch (JsonException)
        {
            return ParsedLine.Other;
        }
    }

    private static string? ItemType(JsonElement parameters)
        => parameters.TryGetProperty("item", out JsonElement item) ? ReadString(item, "type") : null;

    private static string? ReadItemString(JsonElement parameters, string property)
        => parameters.TryGetProperty("item", out JsonElement item) ? ReadString(item, property) : null;

    private static string? ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static LlmUsage? ParseUsage(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("tokenUsage", out JsonElement usage)
            || !usage.TryGetProperty("last", out JsonElement last))
        {
            return null;
        }

        int input = ReadInt(last, "inputTokens");
        int cached = ReadInt(last, "cachedInputTokens");
        int output = ReadInt(last, "outputTokens");

        // `inputTokens` is the WHOLE prompt here, cached part included, where LlmUsage.InputTokens
        // is the uncached remainder only.
        return new LlmUsage(Math.Max(0, input - cached), output) { CacheReadTokens = cached };
    }

    private static int ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int parsed) ? parsed : 0;

    /// <summary>
    /// Renders one of the server's error objects as a kind and a sentence. The agent forwards an
    /// upstream failure by putting the provider's whole JSON error body in <c>message</c> and
    /// classifying it only as <c>other</c> — raw JSON in a canvas balloon is something nobody
    /// reads, and "other" tells the pipeline nothing, so when the body is recognisable the shared
    /// mapper reads it instead: it names the status and pulls out the human sentence.
    /// </summary>
    private static (LlmErrorKind Kind, string Message) DescribeAgentError(JsonElement error, string fallback)
    {
        string raw = (ReadString(error, "message") ?? fallback).Trim();
        LlmErrorKind kind = MapErrorInfo(error);

        if (TryParseUpstreamStatus(raw, out int status))
        {
            // The server's own taxonomy wins when it said something specific; Network is its
            // catch-all, and that is exactly where the upstream status knows better.
            if (kind == LlmErrorKind.Network)
            {
                kind = HttpErrorMapper.MapStatusCode((HttpStatusCode)status);
            }

            return (kind, Summarise(HttpErrorMapper.Describe((HttpStatusCode)status, raw)));
        }

        return (kind, Summarise(raw));
    }

    private static bool TryParseUpstreamStatus(string message, out int status)
    {
        status = 0;
        if (message.Length == 0 || message[0] != '{')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(message);
            return doc.RootElement.TryGetProperty("status", out JsonElement statusEl)
                && statusEl.TryGetInt32(out status)
                && status >= 400;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Maps the server's own error taxonomy onto Physalia's. The variants that carry an upstream
    // HTTP status defer to the shared status mapper rather than guessing a second time.
    private static LlmErrorKind MapErrorInfo(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("codexErrorInfo", out JsonElement info))
        {
            return LlmErrorKind.Network;
        }

        if (info.ValueKind == JsonValueKind.String)
        {
            return info.GetString() switch
            {
                "unauthorized" => LlmErrorKind.Auth,
                "usageLimitExceeded" => LlmErrorKind.RateLimit,
                "serverOverloaded" => LlmErrorKind.RateLimit,
                "contextWindowExceeded" => LlmErrorKind.InvalidRequest,
                "badRequest" => LlmErrorKind.InvalidRequest,
                "cyberPolicy" => LlmErrorKind.InvalidRequest,
                _ => LlmErrorKind.Network,
            };
        }

        if (info.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty variant in info.EnumerateObject())
            {
                if (variant.Value.ValueKind == JsonValueKind.Object
                    && variant.Value.TryGetProperty("httpStatusCode", out JsonElement status)
                    && status.TryGetInt32(out int code))
                {
                    return HttpErrorMapper.MapStatusCode((HttpStatusCode)code);
                }
            }
        }

        return LlmErrorKind.Network;
    }

    private static string DescribeRpcError(JsonElement error)
    {
        string? message = ReadString(error, "message");
        return Summarise(message ?? error.ToString());
    }

    private static string CreateIsolatedWorkingDirectory()
    {
        // A stable, empty temp folder. CreateDirectory is idempotent, so reusing it across runs is
        // fine; an empty workspace gives the agent nothing to auto-discover.
        string dir = Path.Combine(Path.GetTempPath(), "physalia-codex");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Runs `codex --version` once per process and parses the version number out of its output
    // (e.g. "0.142.3" from "codex-cli 0.142.3"). Returns null if the binary is missing, the output
    // cannot be parsed, or the check does not complete within the short timeout.
    private static Version? DetectCliVersion()
    {
        try
        {
            if (!TryResolveExecutable(out string executable))
            {
                // On Unix the bare name resolves at exec time from a richer login PATH; on Windows
                // an unresolved binary means we genuinely cannot determine a version.
                if (OperatingSystem.IsWindows())
                {
                    return null;
                }

                executable = "codex";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--version");

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return null;
            }

            // A short timeout so a hung CLI can never block startup. --version writes one short
            // line, well within the pipe buffer, so waiting before reading cannot deadlock.
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort — the process may already have exited.
                }

                return null;
            }

            return ParseVersion(process.StandardOutput.ReadToEnd());
        }
        catch
        {
            // Any failure (missing binary, launch error) leaves the version undetermined.
            return null;
        }
    }

    private static Version? ParseVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        // The first whitespace-delimited token that parses as a version wins — "codex-cli" does
        // not, "0.142.3" does.
        foreach (string token in output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Version.TryParse(token, out Version? version))
            {
                return version;
            }
        }

        return null;
    }

    // Turns a generic failure message into a version message when the installed CLI is older than
    // the one this session was verified against. When the version is undetermined the generic
    // message is kept, so we never claim a specific mismatch we cannot substantiate.
    private static string DescribeFailure(string generic)
    {
        Version? found = _cliVersion.Value;
        if (found is null || found >= MinVerifiedVersion)
        {
            return generic;
        }

        return $"{generic} The installed Codex CLI is v{found}; Physalia's app-server integration is "
            + $"verified against v{MinVerifiedVersion}+. Upgrade with: npm install -g @openai/codex";
    }

    private static string ResolveExecutable()
    {
        if (TryResolveExecutable(out string resolved))
        {
            return resolved;
        }

        // On Unix/macOS fall back to the bare name so the OS resolves `codex` from PATH at exec
        // time (the PATH scan can miss shells with a richer login PATH); the process start
        // surfaces a clear error if it is genuinely absent.
        if (!OperatingSystem.IsWindows())
        {
            return "codex";
        }

        throw new InvalidOperationException(
            "Codex CLI not found on PATH. Install Codex and run `codex login`.");
    }

    // Scans PATH for the codex executable. On Windows the executable must be a concrete file: the
    // native installer drops `codex.exe`, the npm global install drops a `codex.cmd` shim.
    private static bool TryResolveExecutable(out string path)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { "codex.exe", "codex.cmd", "codex.bat", "codex" }
            : new[] { "codex" };
        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (string directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (string candidate in candidates)
            {
                string full;
                try
                {
                    full = Path.Combine(directory.Trim(), candidate);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (File.Exists(full))
                {
                    path = full;
                    return true;
                }
            }
        }

        path = string.Empty;
        return false;
    }

    private static string Summarise(string error)
    {
        const int max = 500;
        string trimmed = error.Trim();
        return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max) + "…";
    }

    private static Result<LlmResponseChunk, LlmError> Ok(LlmResponseChunk chunk)
        => new Result<LlmResponseChunk, LlmError>.Ok(chunk);

    private static Result<LlmResponseChunk, LlmError> Fail(LlmErrorKind kind, string message)
        => new Result<LlmResponseChunk, LlmError>.Err(new LlmError(kind, message));

    private enum LineKind
    {
        Other,
        ServerRequest,
        TextDelta,
        ThinkingDelta,
        ThinkingPart,
        ThinkingStop,
        FinalText,
        Usage,
        Error,
        RequestFailed,
        TurnFailed,
        TurnCompleted,
    }

    private readonly record struct ParsedLine(
        LineKind Kind,
        string? Text = null,
        LlmUsage? Usage = null,
        LlmErrorKind ErrorKind = LlmErrorKind.Network,
        long RequestId = 0,
        bool WillRetry = false)
    {
        public static ParsedLine Other { get; } = new(LineKind.Other);
    }
}
