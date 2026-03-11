using Physalia.Core.Providers;

public static class LlmProviderFactory
{
    public static LlmProvider Create(string providerName, string apiKey) =>
        providerName switch
        {
            "anthropic" => new AnthropicProvider(apiKey),
            //"openai" => new OpenAiProvider(apiKey),
            _ => throw new ArgumentException($"Unknown provider: {providerName}", nameof(providerName))
        };
}