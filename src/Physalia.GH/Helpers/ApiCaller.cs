using Physalia.Core;
using Physalia.Core.Config;
using Physalia.Core.Prompts;
using Physalia.Core.Providers;
using System;
using System.Threading.Tasks;

namespace Physalia.GH.Helpers;

public class ApiCaller
{
    private bool _waiting;

    public bool IsWaiting => _waiting;

    /// <summary>
    /// Sends the user's prompt to the Anthropic Claude API on a background thread
    /// to avoid blocking the Grasshopper UI. On completion, marshals the result
    /// back to the UI thread and invokes the appropriate callback.
    /// Ignores duplicate calls while a request is already in flight.
    /// </summary>
    /// <param name="prompt">The natural language description of the desired script.</param>
    /// <param name="keysPath">The file path to physaliaKeys.json containing API credentials.</param>
    /// <param name="onSuccess">Callback invoked on the UI thread with the parsed ScriptResponse on success.</param>
    /// <param name="onError">Callback invoked on the UI thread with the error message on failure.</param>
    public void SendAsync(
        string prompt,
        string keysPath,
        Action<ScriptResponse> onSuccess,
        Action<string> onError)
    {
        if (_waiting) return;
        _waiting = true;

        Task.Run(async () =>
        {
            try
            {
                var resolver = new ApiKeyResolver(keysPath);
                var apiKey = resolver.GetKey("Claude Code");

                var provider = new AnthropicProvider();
                var client = new PhysaliaClient(provider, apiKey);

                var result = await client.GenerateScriptAsync(prompt);

                Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    _waiting = false;
                    onSuccess(result);
                }));
            }
            catch (Exception ex)
            {
                var message = ex.InnerException?.Message ?? ex.Message;
                Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    _waiting = false;
                    onError(message);
                }));
            }
        });
    }
}