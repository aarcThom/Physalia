using Physalia.Core;
using Physalia.Core.Config;
using Physalia.Core.Prompts;
using Physalia.Core.Providers;
using System.Threading.Tasks;

namespace Physalia.GH.Helpers;

public class ApiCaller
{
    private readonly ILlmProvider _provider; // the provider and model

    public ApiCaller(ILlmProvider provider)
    {
        _provider = provider; 
    }

    /// <summary>
    /// Reads the API key from disk and sends the user prompt to the LLM provider on a background thread.
    /// The file I/O is offloaded via Task.Run to avoid blocking the Rhino UI thread.
    /// The HTTP request is inherently async and does not block any thread while awaiting the response.
    /// </summary>
    /// <param name="prompt">The natural language description of the desired script.</param>
    /// <param name="keysPath">The file path to physaliaKeys.json containing API credentials.</param>
    /// <returns>A parsed <see cref="ScriptResponse"/> containing the generated Python cript and its input and output parameter definitions.</returns>
    public async Task<ScriptResponse> SendAsync(string prompt, LlmConfig llmConfig)
    {
        
        var apiKey = llmConfig.ApiKey;
        var model = llmConfig.ModelId;

        // The HTTP request is async - ie. no thread blocked while waiting for LLM API
        var client = new PhysaliaClient(_provider, model, apiKey);
        return await client.GenerateScriptAsync(prompt);
    }
}