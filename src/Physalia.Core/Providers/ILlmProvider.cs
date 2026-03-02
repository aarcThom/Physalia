using Physalia.Core.Prompts;

namespace Physalia.Core.Providers
{
    public interface ILlmProvider
    {
        /// <summary>
        /// Sends a user prompt to the LLM and returns the raw text response.
        /// </summary>
        /// <param name="systemPrompt"></param>
        /// <param name="userPrompt"></param>
        /// <param name="apiKey"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<string> SendPromptAsync(
            string systemPrompt,
            string userPrompt,
            string apiKey,
            CancellationToken cancellationToken = default);
    }
}
