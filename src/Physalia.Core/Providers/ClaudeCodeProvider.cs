// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Providers;

/// <summary>
/// Claude Code CLI implementation of <see cref="LlmProvider"/> that invokes the locally
/// installed <c>claude</c> CLI as a subprocess rather than calling the Anthropic HTTP API directly.
/// Authentication is handled by the user's existing <c>claude auth login</c> session — no API key
/// is required or used.
/// </summary>
/// <remarks>
/// Requires the Claude Code CLI to be installed and authenticated on the host machine.
/// See: https://code.claude.com/docs/en/cli-reference.
/// </remarks>
internal class ClaudeCodeProvider : LlmProvider
{

    // NOTE: THIS IS HARDCODED. ENSURE THIS IS UPDATED AS NEW MODELS RELEASE WITH CLAUDE CODE.
    private static readonly List<string> KnownModels = new ()
    {
        "claude-opus-4-6",
        "claude-sonnet-4-6",
        "claude-haiku-4-5-20251001",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeCodeProvider"/> class.
    /// </summary>
    /// <param name="apiKey">Unused — the Claude Code CLI authenticates via the local session. Pass any non-null value.</param>
    public ClaudeCodeProvider(string apiKey)
        : base(apiKey)
    {
    }

    /// <summary>
    /// Gets the display name of the Claude Code CLI provider.
    /// </summary>
    public override string ProviderName => "claude code";

    /// <summary>
    /// Gets the maximum number of output tokens.
    /// </summary>
    /// <remarks>
    /// Not passed to the CLI directly — defined here to satisfy the base class contract.
    /// </remarks>
    public override int MaxTokens => 4096;

    /// <summary>
    /// Populates <see cref="_models"/> with a static list of known Claude models available via the CLI.
    /// </summary>
    /// <returns>A completed task.</returns>
    public override Task GetModelsAsync()
    {
        _models = new List<string>(KnownModels);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Invokes the Claude Code CLI with the conversation history serialized into the prompt and returns the response text.
    /// The CLI does not support native multi-turn message arrays, so prior turns are formatted inline.
    /// For single-turn use the prompt is passed as-is.
    /// </summary>
    /// <param name="systemPrompt">The system prompt passed via <c>--system-prompt</c>.</param>
    /// <param name="history">The ordered list of conversation messages to send.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The result text from the CLI JSON response.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the CLI process cannot be started, exits with a non-zero code,
    /// the response cannot be deserialized, or the subtype is not "success".
    /// </exception>
    protected override async Task<string> SendConversationCoreAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken)
    {
        // The CLI only accepts a single -p prompt, so serialize multi-turn history inline.
        string userPrompt = history.Count == 1
            ? history[0].Content
            : SerializeHistory(history);

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // ArgumentList handles all quoting/escaping correctly regardless of content
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(userPrompt);
        psi.ArgumentList.Add("--system-prompt");
        psi.ArgumentList.Add(systemPrompt);
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(CurrentModel);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

        using var process = new Process { StartInfo = psi };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the Claude Code CLI process.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Claude Code CLI exited with code {process.ExitCode}: {error}");
        }

        var result = JsonSerializer.Deserialize<CliResult>(output)
            ?? throw new InvalidOperationException("Failed to deserialize Claude Code CLI response.");

        if (result.Subtype != "success")
        {
            throw new InvalidOperationException(
                $"Claude Code CLI returned a non-success result: {result.Subtype}");
        }

        return result.Result;
    }

    private static string SerializeHistory(IReadOnlyList<ConversationMessage> history)
    {
        var sb = new StringBuilder();
        foreach (var message in history)
        {
            string label = message.Role == "assistant" ? "Assistant" : "User";
            sb.AppendLine($"[{label}]");
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        sb.AppendLine("Continue from the conversation above. Respond as the assistant.");
        return sb.ToString();
    }

    /// <summary>
    /// Response envelope returned by <c>claude --output-format json</c>.
    /// </summary>
    private class CliResult
    {
        /// <summary>
        /// Gets or sets the message type (always "result" for the final output).
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the result subtype — "success" on a clean run.
        /// </summary>
        [JsonPropertyName("subtype")]
        public string Subtype { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the response text produced by the model.
        /// </summary>
        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;
    }
}
