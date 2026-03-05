namespace Physalia.Core.Providers
{
  /// <summary>
  /// Implementations must use a static readonly HttpClient to avoid socket exhaustion.
  /// See: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines
  /// </summary>
    public interface ILlmProvider
    {
        /// <summary>
        /// Sends a system prompt and user prompt to the LLM and returns the raw text response.
        /// The system prompt defines the behaviour and response format expected from the model.
        /// The user prompt is the natural language instruction provided by the Grasshopper component.
        /// Implementations are responsible for serialising the request and deserialising the raw
        /// response — the returned string is passed directly to <see cref="Physalia.Core.Parsing.ResponseParser"/>.
        /// </summary>
        /// <param name="systemPrompt">Instructions that define the model's behaviour and expected response format.</param>
        /// <param name="userPrompt">The user's instructions.</param>
        /// <param name="apiKey">The API key for authenticating with the provider.</param>
        /// <param name="model">The model to use with the provider</param>
        /// <param name="cancellationToken">Token for cancelling the request if the component is reset or disposed.</param>
        /// <returns>The raw text response from the model, expected to be a JSON object matching the ScriptResponse schema.</returns>
        Task<string> SendPromptAsync(
            string systemPrompt,
            string userPrompt,
            string apiKey,
            string model,
            CancellationToken cancellationToken = default);
    }
}
