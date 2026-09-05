// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Config;

/// <summary>
/// What kind of thing a provider entry configures.
/// </summary>
public enum ProviderKind
{
    /// <summary>
    /// A chat-model provider. At least one must be configured before the pipeline can run, which is
    /// what the chat window's first-run setup screen is waiting for.
    /// </summary>
    Llm,

    /// <summary>
    /// A key for a web tool (Web Search, Read URL). Useful, but it does not satisfy the first-run
    /// requirement — a pipeline with only a Tavily key can call no model.
    /// </summary>
    Tool,
}

/// <summary>
/// How a provider proves it is available.
/// </summary>
public enum ProviderAuth
{
    /// <summary>
    /// The user supplies an endpoint and/or a key, which Physalia stores.
    /// </summary>
    Credential,

    /// <summary>
    /// Nothing is stored: availability is probed live (a CLI on PATH, a local server answering).
    /// The setup page offers a Detect button that forces the probe rather than saving a flag —
    /// a stored flag would keep claiming a CLI exists after it was uninstalled.
    /// </summary>
    Detected,
}

/// <summary>
/// One provider Physalia knows how to reach.
/// </summary>
/// <param name="Id">
/// The provider id. This is the single vocabulary shared by the credential store, the resolver, the
/// chat window's bridge verbs and the setup page's own <c>providers.ts</c> entries. One vocabulary
/// end to end: the two translation tables this replaced had to agree with each other and nothing
/// enforced that they did.
/// </param>
/// <param name="Label">Human-readable name, for runtime messages and the Picker.</param>
/// <param name="Kind">Whether this is a chat-model provider or a web-tool key.</param>
/// <param name="Auth">Whether it is stored or probed.</param>
/// <param name="DefaultBaseUrl">
/// The endpoint prefilled on the setup form, or empty when the provider has no URL (a tool key) or
/// no default worth guessing ("other").
/// </param>
/// <param name="EnvVars">
/// Environment variables consulted before the store, in order. Keeping keys out of any file at all
/// is the strongest option available and stays the first one tried.
/// </param>
public record ProviderInfo(
    string Id,
    string Label,
    ProviderKind Kind,
    ProviderAuth Auth,
    string DefaultBaseUrl,
    IReadOnlyList<string> EnvVars)
{
    /// <summary>
    /// Gets a value indicating whether the setup form should offer an endpoint box.
    /// </summary>
    public bool NeedsUrl => this.Auth == ProviderAuth.Credential && this.Kind != ProviderKind.Tool;
}

/// <summary>
/// The providers Physalia can be configured against.
/// </summary>
/// <remarks>
/// <para>Physalia ships no catalog of MCP servers, deliberately — but it does ship this, because a
/// provider is not a third-party integration the user discovers, it is one of a handful of endpoints
/// the plug-in speaks the protocol of. The list stays small on purpose; anything not on it is
/// reachable through <c>other</c>, which is a plain endpoint plus key.</para>
/// <para><b>Keep in step with <c>src/Physalia.UI/src/lib/chat/providers.ts</c></b>, which holds the
/// same ids plus the setup guide prose (steps, console links, blurbs). Ids are the contract; the UI
/// owns the words, this owns the wiring.</para>
/// </remarks>
public static class ProviderCatalog
{
    private static readonly ProviderInfo[] Entries =
    {
        // ---- Detected: nothing stored, probed live. ---------------------------------------------
        new("claude-code", "Claude Code (subscription)", ProviderKind.Llm, ProviderAuth.Detected,
            string.Empty, Array.Empty<string>()),
        new("codex", "Codex (subscription)", ProviderKind.Llm, ProviderAuth.Detected,
            string.Empty, Array.Empty<string>()),
        new("local-llm", "Local LLM", ProviderKind.Llm, ProviderAuth.Detected,
            "http://127.0.0.1:8080/v1", Array.Empty<string>()),

        // ---- Credentialed chat providers. -------------------------------------------------------
        new("anthropic", "Anthropic", ProviderKind.Llm, ProviderAuth.Credential,
            "https://api.anthropic.com/v1", new[] { "ANTHROPIC_API_KEY" }),
        new("google", "Google (Gemini)", ProviderKind.Llm, ProviderAuth.Credential,
            "https://generativelanguage.googleapis.com/v1beta",
            new[] { "GEMINI_API_KEY", "GOOGLE_API_KEY" }),
        new("openai", "OpenAI", ProviderKind.Llm, ProviderAuth.Credential,
            "https://api.openai.com/v1", new[] { "OPENAI_API_KEY" }),
        new("deepseek", "Deepseek", ProviderKind.Llm, ProviderAuth.Credential,
            "https://api.deepseek.com/v1", new[] { "DEEPSEEK_API_KEY" }),
        new("openrouter", "Open Router", ProviderKind.Llm, ProviderAuth.Credential,
            "https://openrouter.ai/api/v1", new[] { "OPENROUTER_API_KEY" }),

        // Alibaba's endpoint is regional — Singapore by default, with Beijing and Virginia hosts the
        // user may need instead. That variance is exactly why the setup form has an editable URL box
        // rather than a key box and a hardcoded host.
        new("alibaba", "Alibaba Cloud (Qwen)", ProviderKind.Llm, ProviderAuth.Credential,
            "https://dashscope-intl.aliyuncs.com/compatible-mode/v1",
            new[] { "DASHSCOPE_API_KEY" }),

        // Z.AI splits its endpoints by PLAN, not by region: a Coding Plan key is rejected at the
        // general endpoint and vice versa. The setup guide says so; the URL box is how the user acts
        // on it.
        new("zai", "Z.AI (GLM)", ProviderKind.Llm, ProviderAuth.Credential,
            "https://api.z.ai/api/paas/v4",
            new[] { "ZAI_API_KEY", "Z_AI_API_KEY" }),

        new("moonshot", "Moonshot AI (Kimi)", ProviderKind.Llm, ProviderAuth.Credential,
            "https://api.moonshot.ai/v1", new[] { "MOONSHOT_API_KEY" }),

        // Any other OpenAI-compatible endpoint: Ollama, Groq, vLLM, a company gateway. No default
        // URL to guess at, and the key is optional because a local runtime usually wants none.
        new("other", "Other (OpenAI-compatible)", ProviderKind.Llm, ProviderAuth.Credential,
            string.Empty, Array.Empty<string>()),

        // ---- Web-tool keys. ---------------------------------------------------------------------
        new("tavily", "Tavily (web search)", ProviderKind.Tool, ProviderAuth.Credential,
            string.Empty, new[] { "TAVILY_API_KEY" }),
        new("jina", "Jina (read URL)", ProviderKind.Tool, ProviderAuth.Credential,
            string.Empty, new[] { "JINA_API_KEY" }),
    };

    /// <summary>
    /// Gets every known provider, in setup-screen order.
    /// </summary>
    public static IReadOnlyList<ProviderInfo> All => Entries;

    /// <summary>
    /// Finds a provider by id.
    /// </summary>
    /// <param name="id">The provider id, case-insensitive.</param>
    /// <returns>The entry, or null when the id is not one Physalia knows.</returns>
    public static ProviderInfo? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the providers whose credentials Physalia stores (as opposed to probing for).
    /// </summary>
    /// <returns>The storable entries.</returns>
    public static IEnumerable<ProviderInfo> Credentialed() =>
        Entries.Where(e => e.Auth == ProviderAuth.Credential);
}
