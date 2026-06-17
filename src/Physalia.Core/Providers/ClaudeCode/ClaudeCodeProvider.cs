// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Models;
using Physalia.Core.Models.Named;

namespace Physalia.Core.Providers.ClaudeCode;

/// <summary>
/// Provider that runs inference through the locally-installed Claude Code CLI (<c>claude</c>)
/// as a subprocess rather than calling an HTTP API. Authentication is handled by the user's
/// existing <c>claude auth login</c> session, so no API key is required or used. Use with
/// <see cref="ClaudeCodeConfig"/>.
/// </summary>
/// <remarks>
/// Requires the Claude Code CLI to be installed and authenticated on the host machine. The prompt
/// is delivered on standard input (sidestepping command-line length limits and quoting) and the
/// reply is read in a single shot via <c>--output-format json</c>, then yielded as one final chunk.
/// </remarks>
public sealed class ClaudeCodeProvider : ILlmProvider
{
    private const string ContinuationInstruction =
        "Continue from the conversation above. Respond as the assistant.";

    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="tools"/> is ignored: the single-shot CLI invocation does not advertise
    /// tool definitions to the model.
    /// </remarks>
    public async IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
        Conversation conversation,
        string systemPrompt,
        ModelConfig config,
        IReadOnlyList<ToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (config is not ClaudeCodeConfig claudeConfig)
        {
            yield return new Result<LlmResponseChunk, LlmError>.Err(
                new LlmError(LlmErrorKind.InvalidRequest, "ClaudeCodeProvider requires a ClaudeCodeConfig."));
            yield break;
        }

        string prompt = BuildPrompt(conversation);
        yield return await RunCliAsync(prompt, systemPrompt, claudeConfig, ct);
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

    private static string BuildPrompt(Conversation conversation)
    {
        if (conversation.Count == 1)
        {
            return ConversationHelpers.ToContentString(conversation.Messages[0]);
        }

        // The CLI takes a single prompt, so multi-turn history is serialised inline.
        string history = ConversationHelpers.ToDisplayString(conversation);
        return string.IsNullOrEmpty(history)
            ? ContinuationInstruction
            : $"{history}\n\n{ContinuationInstruction}";
    }

    private static async Task<Result<LlmResponseChunk, LlmError>> RunCliAsync(
        string prompt,
        string systemPrompt,
        ClaudeCodeConfig config,
        CancellationToken ct)
    {
        // The system prompt (often large — e.g. an embedded JSON schema) is written to a temp file
        // and passed by path. Passing it as an argument blows past the Windows command-line length
        // limit (a "filename or extension is too long" Win32 failure). The user prompt rides stdin
        // for the same reason.
        string? systemPromptFile = null;

        try
        {
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                systemPromptFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(systemPromptFile, systemPrompt, ct);
            }

            ProcessStartInfo startInfo;
            try
            {
                startInfo = BuildStartInfo(systemPromptFile, config.ModelId);
            }
            catch (FileNotFoundException ex)
            {
                return Fail(LlmErrorKind.Network, ex.Message);
            }

            using var process = new Process { StartInfo = startInfo };

            try
            {
                if (!process.Start())
                {
                    return Fail(LlmErrorKind.Network, "Failed to start the Claude Code CLI process.");
                }
            }
            catch (Exception ex)
            {
                return Fail(
                    LlmErrorKind.Network,
                    $"Could not launch the Claude Code CLI: {ex.Message}. " +
                    "Install Claude Code and run `claude auth login`.");
            }

            try
            {
                await process.StandardInput.WriteAsync(prompt);
                process.StandardInput.Close();

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync(ct);
                Task<string> errorTask = process.StandardError.ReadToEndAsync(ct);

                await process.WaitForExitAsync(ct);

                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode != 0)
                {
                    return Fail(
                        LlmErrorKind.Network,
                        $"Claude Code CLI exited with code {process.ExitCode}: {Summarise(error)}");
                }

                return ParseResponse(output);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return Fail(LlmErrorKind.Cancelled, "The Claude Code CLI call was cancelled.");
            }
            catch (Exception ex)
            {
                return Fail(LlmErrorKind.Network, $"Claude Code CLI call failed: {ex.Message}");
            }
        }
        finally
        {
            if (systemPromptFile is not null)
            {
                try
                {
                    File.Delete(systemPromptFile);
                }
                catch
                {
                    // Best effort — temp file cleanup.
                }
            }
        }
    }

    private static Result<LlmResponseChunk, LlmError> ParseResponse(string output)
    {
        CliResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CliResult>(output);
        }
        catch (JsonException ex)
        {
            return Fail(LlmErrorKind.InvalidRequest, $"Could not parse the Claude Code CLI response: {ex.Message}");
        }

        if (parsed is null)
        {
            return Fail(LlmErrorKind.InvalidRequest, "The Claude Code CLI returned an empty response.");
        }

        if (!string.Equals(parsed.Subtype, "success", StringComparison.Ordinal))
        {
            return Fail(LlmErrorKind.InvalidRequest, $"The Claude Code CLI returned a non-success result: {parsed.Subtype}");
        }

        LlmUsage usage = parsed.Usage is null
            ? new LlmUsage(0, 0)
            : new LlmUsage(parsed.Usage.InputTokens, parsed.Usage.OutputTokens);

        var chunk = new LlmResponseChunk(parsed.Result, IsLast: true, usage);
        return new Result<LlmResponseChunk, LlmError>.Ok(chunk);
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

        // The prompt is sent on stdin; only the flags are passed as arguments. `--system-prompt-file`
        // replaces the CLI's default system prompt with the contents of the given file. `-p` (print
        // mode) is last and value-less so the piped stdin is taken as the prompt.
        if (systemPromptFile is not null)
        {
            startInfo.ArgumentList.Add("--system-prompt-file");
            startInfo.ArgumentList.Add(systemPromptFile);
        }

        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelId);
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("json");
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

        throw new FileNotFoundException(
            "Claude Code CLI not found on PATH. Install Claude Code and run `claude auth login`.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort — the process may already have exited.
        }
    }

    private static string Summarise(string error)
    {
        const int max = 500;
        string trimmed = error.Trim();
        return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max) + "…";
    }

    private static Result<LlmResponseChunk, LlmError> Fail(LlmErrorKind kind, string message)
        => new Result<LlmResponseChunk, LlmError>.Err(new LlmError(kind, message));

    /// <summary>
    /// Response envelope returned by <c>claude --output-format json</c>.
    /// </summary>
    private sealed class CliResult
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("subtype")]
        public string Subtype { get; set; } = string.Empty;

        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;

        [JsonPropertyName("usage")]
        public CliUsage? Usage { get; set; }
    }

    /// <summary>
    /// Token usage block within the CLI JSON response, when present.
    /// </summary>
    private sealed class CliUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }
}
