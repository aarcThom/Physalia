// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Models.Named;

/// <summary>
/// Configuration for inference through the locally-installed OpenAI Codex CLI (<c>codex</c>).
/// Authentication is handled by the user's existing <c>codex login</c> session (a ChatGPT plan or
/// an API-key login held in <c>CODEX_HOME</c>), so <see cref="Physalia.Core.Models.ModelConfig.ApiKey"/>
/// is always empty — no key is required or used.
/// </summary>
/// <remarks>
/// Unlike the HTTP-based configs this is not a protocol config: there is no base URL, and the CLI's
/// app-server exposes no temperature/top-p/top-k knobs, so none are carried here. The one sampling
/// control it does expose is <see cref="ReasoningEffort"/>.
/// </remarks>
/// <param name="ModelId">
/// The Codex model to use, e.g. <c>gpt-6-astra</c>. Empty means "whatever the installed CLI defaults
/// to" — the robust choice, since the CLI resolves its own current default and the model list is
/// account-dependent (see <see cref="KnownModels"/>).
/// </param>
/// <param name="MaxTokens">
/// Informational only — the CLI's app-server takes no max-tokens setting, so this is not sent.
/// </param>
public record CodexConfig(string ModelId = "", int MaxTokens = 8192)
    : ModelConfig(ModelId, "", MaxTokens)
{
    /// <summary>
    /// A seed model list for the Codex Model component's Picker, used until the live list arrives
    /// (and as the fallback when the CLI cannot be queried). The authoritative list comes from the
    /// CLI's <c>model/list</c> call — never hard-code a newer generation in here.
    ///
    /// <para>Which models are on offer is decided by the server per ACCOUNT PLAN and per INSTALLED
    /// CLI VERSION (the model cache is keyed on <c>client_version</c>), so a model that a newer
    /// Codex lists is rejected outright by an older one: "The 'gpt-5.6-sol' model requires a newer
    /// version of Codex" — a real 400, measured on 0.142.3, which is the generation this seed
    /// reflects. That is exactly why the component asks the CLI instead of shipping a list: the
    /// live answer is the set that will actually run.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> KnownModels = new[] { "gpt-5.5", "gpt-5.4", "gpt-5.4-mini" };

    /// <summary>
    /// The reasoning-effort levels offered by the Codex Model component's Picker. The protocol
    /// takes an arbitrary string (each model advertises its own set), so any other value the
    /// installed CLI recognises is passed through unchanged.
    ///
    /// <para>This is the UNION of what the current generation offers, not one model's set: the
    /// live <c>model/list</c> answer carries a <c>supportedReasoningEfforts</c> array per model,
    /// and they differ — <c>gpt-6-astra</c> and the <c>gpt-5.6</c> pair take all six, while
    /// <c>gpt-5.6-luna</c> stops at <c>max</c>. A union is safe because an effort the chosen model
    /// does not support is IGNORED rather than rejected (measured on codex-cli 0.153.4: <c>ultra</c>
    /// against <c>gpt-5.6-luna</c> completes the turn normally), which is the opposite of a
    /// too-new MODEL id — that one is a hard 400. So the model list is asked for live and this one
    /// is not.</para>
    ///
    /// <para><c>max</c> and <c>ultra</c> arrived with the GPT-6 generation; both were verified
    /// accepted on <c>gpt-6-astra</c>, and both produce reasoning summaries where the model's own
    /// default effort (<c>low</c> on Astra) produces none.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> KnownReasoningEfforts =
        new[] { "low", "medium", "high", "xhigh", "max", "ultra" };

    /// <summary>
    /// Gets the reasoning effort to request per turn, or null to leave the model's own default in
    /// place. See <see cref="KnownReasoningEfforts"/>.
    /// </summary>
    public string? ReasoningEffort { get; init; }
}
