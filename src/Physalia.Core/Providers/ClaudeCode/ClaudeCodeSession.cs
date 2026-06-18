// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Providers.ClaudeCode;

/// <summary>
/// A single long-lived <c>claude</c> CLI process held open in streaming-input mode
/// (<c>--input-format stream-json --output-format stream-json</c>), so the heavy harness
/// cold start is paid once and amortised across every turn of one conversation. Each call to
/// <see cref="SendTurnAsync"/> writes one user message on stdin and streams the reply back until
/// the CLI's <c>result</c> line. The CLI keeps conversation context internally, so callers send
/// only the new turn after the seed. Owned and pooled by <see cref="ClaudeCodeProvider"/>.
/// </summary>
/// <remarks>
/// The system prompt is fixed at process start (written to a temp file passed by path, to dodge
/// the Windows command-line length limit), so a changed system prompt requires a fresh session.
/// MCP servers are suppressed (<c>--strict-mcp-config</c>) and the agentic built-in tools are
/// denied so the process behaves as a plain text generator, never running tools of its own.
/// One turn runs at a time, guarded by a semaphore.
/// </remarks>
internal sealed class ClaudeCodeSession : IDisposable
{
    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private Process? _process;
    private string? _systemPromptFile;
    private Task? _stderrPump;
    private volatile string _stderr = string.Empty;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeCodeSession"/> class. The process is
    /// not started until the first <see cref="SendTurnAsync"/> call.
    /// </summary>
    /// <param name="modelId">The Claude model alias or ID the CLI should use.</param>
    /// <param name="systemPrompt">The system prompt, fixed for the life of the process.</param>
    public ClaudeCodeSession(string modelId, string systemPrompt)
    {
        ModelId = modelId;
        SystemPrompt = systemPrompt;
    }

    /// <summary>
    /// Gets the model ID this session's process was started with.
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// Gets the system prompt this session's process was started with.
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
    /// Sends one user turn to the live process and streams the reply chunks until the CLI's
    /// final <c>result</c> line. Text deltas are yielded as they arrive; the final chunk carries
    /// token usage. On success the session stays warm for the next turn.
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
            EnsureStarted();
            Process process = _process!;

            await process.StandardInput.WriteLineAsync(BuildUserLine(content).AsMemory(), ct);
            await process.StandardInput.FlushAsync();

            bool emittedText = false;
            while (true)
            {
                string? raw = await process.StandardOutput.ReadLineAsync(ct);
                if (raw is null)
                {
                    // The process ended in the middle of a turn — treat the session as dead.
                    yield return Fail(LlmErrorKind.Network, $"The Claude Code CLI ended unexpectedly. {Summarise(_stderr)}");
                    yield break;
                }

                ParsedLine parsed = ParseLine(raw);
                switch (parsed.Kind)
                {
                    case LineKind.TextDelta:
                        emittedText = true;
                        yield return new Result<LlmResponseChunk, LlmError>.Ok(
                            new LlmResponseChunk(parsed.Text, IsLast: false, Usage: null));
                        break;

                    case LineKind.ResultError:
                        yield return Fail(LlmErrorKind.InvalidRequest, parsed.Text ?? "The Claude Code CLI returned a non-success result.");
                        yield break;

                    case LineKind.Result:
                        ConsumedMessageCount = newConsumedCount;
                        LastUsedUtc = DateTime.UtcNow;

                        // If no streamed text arrived (e.g. a very short reply slipped through),
                        // fall back to the full result text so the turn is never empty.
                        string? finalDelta = emittedText ? null : parsed.Text;
                        yield return new Result<LlmResponseChunk, LlmError>.Ok(
                            new LlmResponseChunk(finalDelta, IsLast: true, parsed.Usage));
                        yield break;

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
    /// Gets a value indicating whether the underlying process is started and still running.
    /// </summary>
    /// <returns>True when the process is alive.</returns>
    public bool IsAlive => _process is { HasExited: false };

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
                    // Closing stdin lets the CLI exit cleanly; kill the tree if it lingers.
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

        if (_systemPromptFile is not null)
        {
            try
            {
                File.Delete(_systemPromptFile);
            }
            catch
            {
                // Best effort — temp file cleanup.
            }

            _systemPromptFile = null;
        }

        _turnLock.Dispose();
    }

    private void EnsureStarted()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        if (_process is not null)
        {
            throw new InvalidOperationException("The Claude Code CLI session process has exited.");
        }

        if (!string.IsNullOrEmpty(SystemPrompt))
        {
            _systemPromptFile = Path.GetTempFileName();
            File.WriteAllText(_systemPromptFile, SystemPrompt);
        }

        var process = new Process { StartInfo = BuildStartInfo(_systemPromptFile, ModelId) };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start the Claude Code CLI process.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not launch the Claude Code CLI: {ex.Message}. Install Claude Code and run `claude auth login`.", ex);
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

    private static ProcessStartInfo BuildStartInfo(string? systemPromptFile, string modelId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutable(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Streaming both ways keeps the process alive between turns; --include-partial-messages
        // gives token-level deltas; --verbose is required for -p stream-json output.
        startInfo.ArgumentList.Add("--input-format");
        startInfo.ArgumentList.Add("stream-json");
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("stream-json");
        startInfo.ArgumentList.Add("--include-partial-messages");
        startInfo.ArgumentList.Add("--verbose");

        // --system-prompt-file replaces the CLI's default agent system prompt with the file's
        // contents. --strict-mcp-config (with no --mcp-config) loads zero MCP servers, the single
        // biggest startup/context win. The default agentic tools are denied so the process never
        // runs tools of its own — Physalia owns its tool loop.
        if (systemPromptFile is not null)
        {
            startInfo.ArgumentList.Add("--system-prompt-file");
            startInfo.ArgumentList.Add(systemPromptFile);
        }

        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelId);
        startInfo.ArgumentList.Add("--strict-mcp-config");
        startInfo.ArgumentList.Add("--disallowed-tools");
        startInfo.ArgumentList.Add(
            "Task Bash BashOutput Edit MultiEdit Write Read NotebookEdit Glob Grep WebFetch WebSearch Skill");
        startInfo.ArgumentList.Add("-p");

        return startInfo;
    }

    private static string ResolveExecutable()
    {
        // On Unix/macOS the OS resolves `claude` from PATH directly.
        if (!OperatingSystem.IsWindows())
        {
            return "claude";
        }

        // On Windows the executable must be a concrete file: the native installer drops
        // `claude.exe`, the npm global install drops a `claude.cmd` shim. Prefer the former.
        string[] candidates = { "claude.exe", "claude.cmd", "claude.bat", "claude" };
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
                    return full;
                }
            }
        }

        throw new InvalidOperationException(
            "Claude Code CLI not found on PATH. Install Claude Code and run `claude auth login`.");
    }

    private static string BuildUserLine(IReadOnlyList<MessageContent> content)
    {
        // Plain string content for a single text block; a typed block array otherwise (multimodal).
        object contentValue;
        if (content.Count == 1 && content[0] is TextContent only)
        {
            contentValue = only.Text;
        }
        else
        {
            var blocks = new List<object>(content.Count);
            foreach (MessageContent block in content)
            {
                object? mapped = MapBlock(block);
                if (mapped is not null)
                {
                    blocks.Add(mapped);
                }
            }

            // Guard against an all-unmappable turn producing an empty content array.
            contentValue = blocks.Count > 0 ? blocks : string.Empty;
        }

        var message = new
        {
            type = "user",
            message = new
            {
                role = "user",
                content = contentValue,
            },
        };

        // Default (non-indented) serialisation keeps this to a single NDJSON line; any newlines
        // inside text are escaped, so the line framing stays intact.
        return JsonSerializer.Serialize(message);
    }

    private static object? MapBlock(MessageContent block) => block switch
    {
        TextContent text => new { type = "text", text = text.Text },
        ImageContent { Source: InlineImage img } => new
        {
            type = "image",
            source = new
            {
                type = "base64",
                media_type = img.MimeType,
                data = Convert.ToBase64String(img.Data),
            },
        },
        ImageContent { Source: UrlImage url } => new
        {
            type = "image",
            source = new { type = "url", url = url.Url },
        },
        _ => null,
    };

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

            if (!root.TryGetProperty("type", out JsonElement typeEl))
            {
                return ParsedLine.Other;
            }

            string? type = typeEl.GetString();

            if (type == "stream_event"
                && root.TryGetProperty("event", out JsonElement ev)
                && ev.TryGetProperty("type", out JsonElement evType)
                && evType.GetString() == "content_block_delta"
                && ev.TryGetProperty("delta", out JsonElement delta)
                && delta.TryGetProperty("type", out JsonElement deltaType)
                && deltaType.GetString() == "text_delta"
                && delta.TryGetProperty("text", out JsonElement textEl))
            {
                // Only true text deltas carry response content; thinking/signature deltas are skipped.
                return new ParsedLine(LineKind.TextDelta, textEl.GetString(), null);
            }

            if (type == "result")
            {
                string subtype = root.TryGetProperty("subtype", out JsonElement subEl)
                    ? subEl.GetString() ?? string.Empty
                    : string.Empty;

                if (subtype != "success")
                {
                    return new ParsedLine(LineKind.ResultError, $"The Claude Code CLI returned a non-success result: {subtype}", null);
                }

                string? resultText = root.TryGetProperty("result", out JsonElement resEl) ? resEl.GetString() : null;
                LlmUsage usage = ParseUsage(root);
                return new ParsedLine(LineKind.Result, resultText, usage);
            }

            return ParsedLine.Other;
        }
        catch (JsonException)
        {
            return ParsedLine.Other;
        }
    }

    private static LlmUsage ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage))
        {
            return new LlmUsage(0, 0);
        }

        int input = usage.TryGetProperty("input_tokens", out JsonElement inEl) && inEl.TryGetInt32(out int i) ? i : 0;
        int output = usage.TryGetProperty("output_tokens", out JsonElement outEl) && outEl.TryGetInt32(out int o) ? o : 0;
        return new LlmUsage(input, output);
    }

    private static string Summarise(string error)
    {
        const int max = 500;
        string trimmed = error.Trim();
        return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max) + "…";
    }

    private static Result<LlmResponseChunk, LlmError> Fail(LlmErrorKind kind, string message)
        => new Result<LlmResponseChunk, LlmError>.Err(new LlmError(kind, message));

    private enum LineKind
    {
        Other,
        TextDelta,
        Result,
        ResultError,
    }

    private readonly record struct ParsedLine(LineKind Kind, string? Text, LlmUsage? Usage)
    {
        public static ParsedLine Other { get; } = new(LineKind.Other, null, null);
    }
}
