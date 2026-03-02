using Physalia.Core.Config;
using Physalia.Core.Parsing;
using Physalia.Core.Prompts;
using Physalia.Core.Providers;

namespace Physalia.Core;

public class PhysaliaClient
{
    private readonly ILlmProvider _provider;
    private readonly string _apiKey;

    public PhysaliaClient(ILlmProvider provider, string apiKey)
    {
        _provider = provider;
        _apiKey = apiKey;
    }

    /// <summary>
    /// Takes a plain English prompt, sends it to the LLM, and returns
    /// a parsed ScriptResponse with the Python code, inputs, and outputs.
    /// </summary>
    public async Task<ScriptResponse> GenerateScriptAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));

        var rawResponse = await _provider.SendPromptAsync(
            SystemPrompt.Default,
            prompt,
            _apiKey,
            cancellationToken);

        return ResponseParser.Parse(rawResponse);
    }
}