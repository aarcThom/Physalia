// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Providers;

/// <summary>
/// Abstract base class for LLM provider implementations.
/// </summary>
public abstract class LlmProvider
{
    /// <summary>
    /// HTTP client shared across all calls to this provider.
    /// </summary>
    /// <remarks>
    /// HttpClient is designed to be long-lived and reused — one per provider,
    /// shared across all calls. readonly in the abstract base class ensures a single
    /// instance for each inheriting class for the lifetime of the plugin.
    /// See: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines .
    /// </remarks>
    protected readonly HttpClient _http = new ();

    /// <summary>
    /// The API key used to authenticate requests to the provider.
    /// </summary>
    protected readonly string _apiKey;

    /// <summary>
    /// The backing list of models available through the provider.
    /// </summary>
    protected List<string> _models = new ();

    /// <summary>
    /// A read-only list of LLMs available through the provider.
    /// </summary>
    public IReadOnlyList<string> Models => _models;

    /// <summary>
    /// The selected LLM. Will be used for the API request.
    /// </summary>
    public string CurrentModel { get; set; } = string.Empty;

    /// <summary>
    /// The name of the LLM provider.
    /// </summary>
    public abstract string ProviderName { get; }

    /// <summary>
    /// The max tokens set for the provider.
    /// See: https://learn.microsoft.com/en-us/dotnet/ai/conceptual/understanding-tokens .
    /// </summary>
    public abstract int MaxTokens { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmProvider"/> class.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the provider.</param>
    public LlmProvider(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        _apiKey = apiKey;
    }

    /// <summary>
    /// Fetches available models from the provider and populates <see cref="_models"/>.
    /// Must be called after construction before <see cref="Models"/> is accessed.
    /// </summary>
    /// <remarks>
    /// Implementors must populate <see cref="_models"/> before this task completes.
    /// </remarks>
    /// <returns> The available models from the provider.</placeholder></returns>
    public abstract Task GetModelsAsync();

    /// <summary>
    /// Validates inputs and sends a single-turn prompt to the LLM.
    /// Convenience wrapper around <see cref="SendConversationAsync"/> for single-turn use.
    /// Throws <see cref="ArgumentException"/> if <paramref name="systemPrompt"/>, <paramref name="userPrompt"/>,
    /// or <see cref="CurrentModel"/> are null or whitespace.
    /// </summary>
    /// <param name="systemPrompt">The system prompt that defines the model's behavior and context.</param>
    /// <param name="userPrompt">The user prompt containing the request to be processed.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The raw response string returned by the LLM.</returns>
    public Task<string> SendPromptAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new ArgumentException("User prompt must not be null or empty.", nameof(userPrompt));
        }

        return SendConversationAsync(systemPrompt, new[] { new ConversationMessage("user", userPrompt) }, cancellationToken);
    }

    /// <summary>
    /// Validates inputs and delegates to <see cref="SendConversationCoreAsync"/> for provider-specific execution.
    /// Throws <see cref="ArgumentException"/> if <paramref name="systemPrompt"/>, <paramref name="history"/>,
    /// or <see cref="CurrentModel"/> are null or empty.
    /// </summary>
    /// <param name="systemPrompt">The system prompt that defines the model's behavior and context.</param>
    /// <param name="history">The ordered list of conversation messages to send.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The raw response string returned by the LLM.</returns>
    public Task<string> SendConversationAsync(string systemPrompt, IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new ArgumentException("System prompt must not be null or empty.", nameof(systemPrompt));
        }

        if (history == null || history.Count == 0)
        {
            throw new ArgumentException("Conversation history must not be null or empty.", nameof(history));
        }

        if (string.IsNullOrWhiteSpace(CurrentModel))
        {
            throw new ArgumentException("Current model cannot be null or empty.", nameof(CurrentModel));
        }

        return SendConversationCoreAsync(systemPrompt, history, cancellationToken);
    }

    /// <summary>
    /// Provider-specific implementation for sending a multi-turn conversation to the LLM.
    /// Called by <see cref="SendConversationAsync"/> after input validation.
    /// </summary>
    /// <param name="systemPrompt">The system prompt that defines the model's behavior and context.</param>
    /// <param name="history">The ordered list of conversation messages to send.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The raw response string returned by the LLM.</returns>
    protected abstract Task<string> SendConversationCoreAsync(string systemPrompt, IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken);
}