// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Models.Named;

/// <summary>
/// Configuration for inference through the locally-installed Claude Code CLI (<c>claude</c>).
/// Authentication is handled by the user's existing <c>claude auth login</c> session, so
/// <see cref="Physalia.Core.Models.ModelConfig.ApiKey"/> is always empty — no key is required or used.
/// </summary>
/// <remarks>
/// Unlike the HTTP-based configs this is not a protocol config: there is no base URL, and the
/// CLI's print mode exposes no temperature/top-p/top-k flags, so none are carried here.
/// </remarks>
/// <param name="ModelId">
/// The Claude model to use. Accepts the CLI aliases in <see cref="KnownModels"/> (<c>opus</c>,
/// <c>sonnet</c>, <c>haiku</c>) or any full model ID the installed CLI recognises.
/// </param>
/// <param name="MaxTokens">
/// Informational only — the CLI print mode takes no max-tokens flag, so this is not sent.
/// </param>
public record ClaudeCodeConfig(string ModelId = "sonnet", int MaxTokens = 8192)
    : ModelConfig(ModelId, "", MaxTokens)
{
    /// <summary>
    /// CLI model aliases offered by the Claude Code Model component's Picker. The installed CLI
    /// resolves each alias to the current release, so this seed list never goes stale.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownModels = new[] { "opus", "sonnet", "haiku" };
}
