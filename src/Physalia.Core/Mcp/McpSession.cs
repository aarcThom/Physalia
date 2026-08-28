// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Mcp;

/// <summary>
/// One live connection to an MCP server: a warm subprocess speaking JSON-RPC 2.0 over stdio.
/// </summary>
/// <remarks>
/// <para><b>Only the stdio transport is implemented, deliberately.</b> The official C# SDK cannot run
/// inside Rhino — measured 2026-08-27: Rhino 8 runs on the .NET 8 shared runtime, which serves
/// <c>System.Text.Json</c> 8.0.0 to every plug-in and ignores any copy shipped beside the
/// <c>.gha</c>, while the SDK's netstandard2.0 asset and its <c>Microsoft.Extensions.AI</c>
/// dependency call .NET 10 APIs (<c>JsonElement.Parse(ReadOnlySpan&lt;byte&gt;, …)</c>). No packaging
/// change can fix that. Remote and OAuth-protected servers are therefore reached by launching the
/// Physalia MCP bridge — itself an ordinary console app that CAN host the SDK — and speaking plain
/// stdio MCP to it. One transport here, one protocol, no new package references.</para>
/// <para>The shape follows <c>CodexSession</c>: a warm process, a background read pump, and request
/// ids correlated through a dictionary of completion sources. Unlike Codex this is fully concurrent —
/// several tool calls may be in flight, and server notifications interleave with responses.</para>
/// </remarks>
public sealed class McpSession : IDisposable
{
    /// <summary>The protocol revision this client asks for; a server may negotiate downward.</summary>
    public const string ProtocolVersion = "2025-06-18";

    // No BOM: a byte-order mark on the first line makes the server's JSON parser choke, the same
    // trap already recorded for the Claude Code pipes.
    private static readonly Encoding _pipeEncoding = new UTF8Encoding(false);

    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<string> _stderr = new();

    private Process? _process;
    private Task? _readPump;
    private Task? _stderrPump;
    private long _nextRequestId;
    private bool _disposed;
    private volatile bool _toolsStale = true;
    private IReadOnlyList<LlmToolDefinition> _tools = Array.Empty<LlmToolDefinition>();

    private McpSession(McpServerDefinition definition)
    {
        Definition = definition;
        LastUsedUtc = DateTime.UtcNow;
    }

    /// <summary>Gets the definition this session was launched from.</summary>
    public McpServerDefinition Definition { get; }

    /// <summary>Gets the time of the most recent call, used by the idle reaper.</summary>
    public DateTime LastUsedUtc { get; private set; }

    /// <summary>Gets a value indicating whether the subprocess is still running.</summary>
    public bool IsAlive => _process is { HasExited: false };

    /// <summary>
    /// Gets the tools most recently listed by the server. Empty until
    /// <see cref="ListToolsAsync"/> has completed once.
    /// </summary>
    public IReadOnlyList<LlmToolDefinition> Tools => _tools;

    /// <summary>
    /// Gets a value indicating whether the server has announced a change to its tool set since the
    /// last listing, meaning the cached <see cref="Tools"/> should be refreshed.
    /// </summary>
    public bool ToolsStale => _toolsStale;

    /// <summary>Gets whatever the server wrote to stderr, for diagnostics on a failed launch.</summary>
    public string StandardError
    {
        get
        {
            lock (_stderr)
            {
                return string.Join(System.Environment.NewLine, _stderr);
            }
        }
    }

    /// <summary>
    /// Launches the server and completes the MCP initialize handshake.
    /// </summary>
    /// <param name="definition">The server to launch.</param>
    /// <param name="bridgeExecutable">
    /// Path to the Physalia MCP bridge, used when <paramref name="definition"/> names a remote URL.
    /// Ignored for a local server; a missing bridge makes a remote definition fail with a message
    /// saying so.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A started, initialized session, or the reason it could not be reached.</returns>
    public static async Task<Result<McpSession, LlmError>> StartAsync(
        McpServerDefinition definition,
        string? bridgeExecutable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.IsRunnable)
        {
            return new Result<McpSession, LlmError>.Err(new LlmError(
                LlmErrorKind.InvalidRequest,
                $"MCP server '{definition.Name}' declares neither a command nor a url."));
        }

        var session = new McpSession(definition);

        try
        {
            session.StartProcess(bridgeExecutable);
            Result<JsonElement, LlmError> handshake = await session.HandshakeAsync(ct).ConfigureAwait(false);

            if (handshake.IsErr(out LlmError? error, out _))
            {
                session.Dispose();
                return new Result<McpSession, LlmError>.Err(error);
            }

            return new Result<McpSession, LlmError>.Ok(session);
        }
        catch (OperationCanceledException)
        {
            session.Dispose();
            return new Result<McpSession, LlmError>.Err(new LlmError(LlmErrorKind.Cancelled, "MCP connection cancelled."));
        }
        catch (Exception ex)
        {
            session.Dispose();
            return new Result<McpSession, LlmError>.Err(new LlmError(
                LlmErrorKind.Network,
                $"Could not start MCP server '{definition.Name}': {ex.Message}{session.DescribeStderr()}"));
        }
    }

    /// <summary>
    /// Asks the server for its tool set and caches it, following pagination to the end.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server's tools, translated into Physalia's own definition type.</returns>
    public async Task<Result<IReadOnlyList<LlmToolDefinition>, LlmError>> ListToolsAsync(CancellationToken ct)
    {
        var collected = new List<LlmToolDefinition>();
        string? cursor = null;

        do
        {
            object parameters = cursor is null ? new { } : new { cursor };
            Result<JsonElement, LlmError> response =
                await SendRequestAsync("tools/list", parameters, ct).ConfigureAwait(false);

            if (response.IsErr(out LlmError? error, out JsonElement result))
            {
                return new Result<IReadOnlyList<LlmToolDefinition>, LlmError>.Err(error);
            }

            if (result.TryGetProperty("tools", out JsonElement tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    if (TryReadTool(tool, out LlmToolDefinition definition))
                    {
                        collected.Add(definition);
                    }
                }
            }

            cursor = result.TryGetProperty("nextCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
        while (!string.IsNullOrEmpty(cursor));

        _tools = collected;
        _toolsStale = false;
        return new Result<IReadOnlyList<LlmToolDefinition>, LlmError>.Ok(collected);
    }

    /// <summary>
    /// Invokes one tool on the server.
    /// </summary>
    /// <param name="toolName">The server's own name for the tool, un-namespaced.</param>
    /// <param name="argumentsJson">The model's arguments as a JSON object string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The call's result, or a transport-level failure.</returns>
    public async Task<Result<McpToolCallResult, LlmError>> CallToolAsync(
        string toolName,
        string argumentsJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name is required.", nameof(toolName));
        }

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = toolName,
            ["arguments"] = ParseArguments(argumentsJson),
        };

        Result<JsonElement, LlmError> response =
            await SendRequestAsync("tools/call", parameters, ct).ConfigureAwait(false);

        return response.IsErr(out LlmError? error, out JsonElement result)
            ? new Result<McpToolCallResult, LlmError>.Err(error)
            : new Result<McpToolCallResult, LlmError>.Ok(ReadCallResult(result));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        foreach (TaskCompletionSource<JsonElement> pending in _pending.Values)
        {
            pending.TrySetCanceled();
        }

        _pending.Clear();

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process is already gone; nothing to kill.
        }

        _process?.Dispose();
        _process = null;
        _lifetime.Dispose();
        _writeLock.Dispose();
    }

    private static object? ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A model that emitted unparseable arguments gets the server's own complaint about
            // missing required fields, which is more useful than a Physalia-side rejection.
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
    }

    private static bool TryReadTool(JsonElement tool, out LlmToolDefinition definition)
    {
        definition = new LlmToolDefinition(string.Empty, string.Empty, "{}");

        if (!tool.TryGetProperty("name", out JsonElement nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string name = nameElement.GetString() ?? string.Empty;
        if (name.Length == 0)
        {
            return false;
        }

        string description = tool.TryGetProperty("description", out JsonElement d) && d.ValueKind == JsonValueKind.String
            ? d.GetString() ?? string.Empty
            : string.Empty;

        // A server may also carry a human-facing title; fold it in when the description is bare so
        // the model has something to decide on.
        if (description.Length == 0 &&
            tool.TryGetProperty("title", out JsonElement t) && t.ValueKind == JsonValueKind.String)
        {
            description = t.GetString() ?? string.Empty;
        }

        string schema = tool.TryGetProperty("inputSchema", out JsonElement s) && s.ValueKind == JsonValueKind.Object
            ? s.GetRawText()
            : "{\"type\":\"object\"}";

        definition = new LlmToolDefinition(name, description, schema);
        return true;
    }

    private static McpToolCallResult ReadCallResult(JsonElement result)
    {
        var texts = new List<string>();
        var attachments = new List<MessageContent>();

        if (result.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement block in content.EnumerateArray())
            {
                string kind = block.TryGetProperty("type", out JsonElement typeElement) &&
                              typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;

                switch (kind)
                {
                    case "text":
                        if (block.TryGetProperty("text", out JsonElement text) &&
                            text.ValueKind == JsonValueKind.String)
                        {
                            texts.Add(text.GetString() ?? string.Empty);
                        }

                        break;

                    case "image":
                        if (TryReadInlineImage(block, out ImageContent? image))
                        {
                            attachments.Add(image);
                        }

                        break;

                    case "resource":
                        // An embedded resource carries either text or a blob; only the text form can
                        // reach the model as a tool result, so a blob is named rather than dropped.
                        texts.Add(ReadEmbeddedResource(block));
                        break;

                    case "resource_link":
                        // Added in the 2025-06-18 revision: a pointer to a resource rather than its
                        // content. The model can only act on what it is told, so render the link.
                        texts.Add(ReadResourceLink(block));
                        break;

                    default:
                        // audio and any future block type: reported, not silently swallowed.
                        texts.Add($"[unsupported content block: {kind}]");
                        break;
                }
            }
        }

        // Some servers answer only with structuredContent. Serialising it keeps the answer intact
        // rather than handing the model an empty result.
        if (texts.Count == 0 && attachments.Count == 0 &&
            result.TryGetProperty("structuredContent", out JsonElement structured))
        {
            texts.Add(structured.GetRawText());
        }

        bool isError = result.TryGetProperty("isError", out JsonElement errorFlag) &&
                       errorFlag.ValueKind == JsonValueKind.True;

        string body = texts.Count > 0
            ? string.Join(System.Environment.NewLine, texts)
            : attachments.Count > 0 ? "(image returned)" : "(no content)";

        return new McpToolCallResult(body, attachments, isError);
    }

    private static bool TryReadInlineImage(JsonElement block, out ImageContent image)
    {
        image = new ImageContent(new InlineImage(Array.Empty<byte>(), "image/png"));

        if (!block.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string mime = block.TryGetProperty("mimeType", out JsonElement m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? "image/png"
            : "image/png";

        try
        {
            image = new ImageContent(new InlineImage(Convert.FromBase64String(data.GetString() ?? string.Empty), mime));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ReadEmbeddedResource(JsonElement block)
    {
        if (!block.TryGetProperty("resource", out JsonElement resource))
        {
            return "[resource]";
        }

        if (resource.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString() ?? string.Empty;
        }

        string uri = resource.TryGetProperty("uri", out JsonElement u) && u.ValueKind == JsonValueKind.String
            ? u.GetString() ?? "unknown"
            : "unknown";

        return $"[binary resource: {uri}]";
    }

    private static string ReadResourceLink(JsonElement block)
    {
        string uri = ReadStringOrEmpty(block, "uri");
        string name = ReadStringOrEmpty(block, "name");
        string description = ReadStringOrEmpty(block, "description");

        string label = name.Length > 0 ? name : uri;
        return description.Length > 0
            ? $"[resource: {label} <{uri}> — {description}]"
            : $"[resource: {label} <{uri}>]";
    }

    private static string ReadStringOrEmpty(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private string DescribeStderr()
    {
        string errors = StandardError;
        return errors.Length == 0 ? string.Empty : $" Server said: {errors}";
    }

    private void StartProcess(string? bridgeExecutable)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = _pipeEncoding,
            StandardOutputEncoding = _pipeEncoding,
            StandardErrorEncoding = _pipeEncoding,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (Definition.IsRemote)
        {
            if (string.IsNullOrWhiteSpace(bridgeExecutable) || !File.Exists(bridgeExecutable))
            {
                throw new FileNotFoundException(
                    "the Physalia MCP bridge is missing from the plug-in folder, so remote servers cannot be reached.",
                    bridgeExecutable ?? "Physalia.McpBridge.exe");
            }

            startInfo.FileName = bridgeExecutable;
            startInfo.ArgumentList.Add("--url");
            startInfo.ArgumentList.Add(Definition.Url!);
        }
        else
        {
            // Resolved to a full path, never handed over as written: Windows' CreateProcess does not
            // apply PATHEXT, so the near-universal `command: npx` would otherwise fail outright.
            startInfo.FileName = McpExecutable.Resolve(Definition.Command)
                ?? throw new FileNotFoundException(McpExecutable.DescribeMissing(Definition.Command));

            foreach (string argument in Definition.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (!string.IsNullOrWhiteSpace(Definition.WorkingDirectory))
        {
            startInfo.WorkingDirectory = Definition.WorkingDirectory;
        }

        foreach (KeyValuePair<string, string> pair in Definition.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");
        }

        _process = process;
        _readPump = Task.Run(() => ReadPumpAsync(_lifetime.Token));
        _stderrPump = Task.Run(() => StderrPumpAsync(_lifetime.Token));
    }

    private async Task<Result<JsonElement, LlmError>> HandshakeAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        var parameters = new
        {
            protocolVersion = ProtocolVersion,
            // Physalia declares NO sampling and NO elicitation. Sampling would let a third-party
            // server spend the user's tokens through an LLM Call with nothing on the canvas
            // recording that it happened, which breaks the rule that every model action is visible
            // as a node. Roots are withheld for the same reason: nothing here needs them yet.
            capabilities = new { },
            clientInfo = new { name = "Physalia", version = "1.0" },
        };

        Result<JsonElement, LlmError> result =
            await SendRequestAsync("initialize", parameters, timeout.Token).ConfigureAwait(false);

        if (result.IsErr(out _, out _))
        {
            return result;
        }

        await SendNotificationAsync("notifications/initialized", timeout.Token).ConfigureAwait(false);
        return result;
    }

    private async Task<Result<JsonElement, LlmError>> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken ct)
    {
        if (!IsAlive)
        {
            return new Result<JsonElement, LlmError>.Err(new LlmError(
                LlmErrorKind.Network,
                $"MCP server '{Definition.Name}' is not running.{DescribeStderr()}"));
        }

        LastUsedUtc = DateTime.UtcNow;

        long id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        try
        {
            await WriteMessageAsync(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters,
                },
                ct).ConfigureAwait(false);

            using CancellationTokenRegistration registration = ct.Register(() => completion.TrySetCanceled());
            JsonElement result = await completion.Task.ConfigureAwait(false);
            LastUsedUtc = DateTime.UtcNow;
            return new Result<JsonElement, LlmError>.Ok(result);
        }
        catch (McpRpcException ex)
        {
            return new Result<JsonElement, LlmError>.Err(new LlmError(
                LlmErrorKind.InvalidRequest,
                $"MCP server '{Definition.Name}' rejected `{method}`: {ex.Message}"));
        }
        catch (OperationCanceledException)
        {
            return new Result<JsonElement, LlmError>.Err(new LlmError(
                ct.IsCancellationRequested ? LlmErrorKind.Cancelled : LlmErrorKind.Timeout,
                $"MCP request `{method}` to '{Definition.Name}' did not complete.{DescribeStderr()}"));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            return new Result<JsonElement, LlmError>.Err(new LlmError(
                LlmErrorKind.Network,
                $"Lost the connection to MCP server '{Definition.Name}': {ex.Message}{DescribeStderr()}"));
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, CancellationToken ct) =>
        WriteMessageAsync(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = new { },
            },
            ct);

    // Answers a server-initiated request. Physalia declares no capabilities, so every one of these
    // is refused — but it MUST be answered: an unanswered server request blocks that side forever,
    // the same trap already hit with the Codex app-server.
    private Task RefuseServerRequestAsync(JsonElement id, CancellationToken ct) =>
        WriteMessageAsync(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonSerializer.Deserialize<object>(id.GetRawText()),
                ["error"] = new
                {
                    code = -32601,
                    message = "Physalia declares no client capabilities; this request is not supported.",
                },
            },
            ct);

    private async Task WriteMessageAsync(object message, CancellationToken ct)
    {
        Process process = _process ?? throw new InvalidOperationException("The MCP session is not started.");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string line = JsonSerializer.Serialize(message);
            await process.StandardInput.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadPumpAsync(CancellationToken ct)
    {
        Process? process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                await DispatchAsync(line, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The process went away; the failure surfaces on the pending requests below.
        }
        finally
        {
            // Never leave a caller waiting on a dead pipe.
            foreach (TaskCompletionSource<JsonElement> pending in _pending.Values)
            {
                pending.TrySetException(new IOException(
                    $"MCP server '{Definition.Name}' closed the connection.{DescribeStderr()}"));
            }
        }
    }

    private async Task DispatchAsync(string line, CancellationToken ct)
    {
        JsonElement message;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            message = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Servers occasionally print banners to stdout before the protocol starts. Ignoring a
            // non-JSON line is correct; treating it as a protocol error would kill usable servers.
            return;
        }

        bool hasId = message.TryGetProperty("id", out JsonElement id) && id.ValueKind != JsonValueKind.Null;
        bool hasMethod = message.TryGetProperty("method", out JsonElement methodElement);

        if (hasId && !hasMethod)
        {
            CompleteResponse(id, message);
            return;
        }

        if (hasId)
        {
            await RefuseServerRequestAsync(id, ct).ConfigureAwait(false);
            return;
        }

        if (hasMethod && methodElement.GetString() == "notifications/tools/list_changed")
        {
            // The server changed its tool set. Marking it stale is enough: the component's next
            // solve re-lists, which flows through the Tools Present signature and expires the
            // Conversation Log by itself.
            _toolsStale = true;
        }
    }

    private void CompleteResponse(JsonElement id, JsonElement message)
    {
        if (!id.TryGetInt64(out long requestId) || !_pending.TryRemove(requestId, out TaskCompletionSource<JsonElement>? completion))
        {
            return;
        }

        if (message.TryGetProperty("error", out JsonElement error))
        {
            string text = error.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? "unknown error"
                : error.GetRawText();

            completion.TrySetException(new McpRpcException(text));
            return;
        }

        completion.TrySetResult(
            message.TryGetProperty("result", out JsonElement result) ? result.Clone() : default);
    }

    private async Task StderrPumpAsync(CancellationToken ct)
    {
        Process? process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                lock (_stderr)
                {
                    // Bounded: a chatty server must not grow this without limit for the life of
                    // the session, and only the first lines matter for diagnosing a bad launch.
                    if (_stderr.Count < 20)
                    {
                        _stderr.Add(line);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Process gone.
        }
    }

    /// <summary>
    /// A JSON-RPC error returned by the server, as distinct from a transport failure.
    /// </summary>
    private sealed class McpRpcException : Exception
    {
        public McpRpcException(string message)
            : base(message)
        {
        }
    }
}
