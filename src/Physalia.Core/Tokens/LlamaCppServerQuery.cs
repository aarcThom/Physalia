// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Physalia.Core.Common;
using Physalia.Core.Models.Protocol;

namespace Physalia.Core.Tokens;

/// <summary>
/// Context window information returned by a llama-server instance.
/// </summary>
/// <param name="NCtxTrain">
/// The context length the model was trained on, read from the GGUF header
/// (<c>llama.context_length</c>). This is the architectural maximum.
/// </param>
/// <param name="NCtx">
/// The context window the server was launched with (<c>--ctx-size</c>).
/// May be smaller than <see cref="NCtxTrain"/> if the server was constrained at startup.
/// </param>
public record LlamaCppServerProps(int NCtxTrain, int NCtx);

/// <summary>
/// Queries llama-server native endpoints for model and context metadata.
/// All methods are stateless — pass the caller's shared <see cref="HttpClient"/>.
/// </summary>
public static class LlamaCppServerQuery
{
    /// <summary>
    /// Retrieves context window information from a running llama-server.
    /// Tries <c>GET /v1/models</c> first (newer llama.cpp exposes <c>meta.n_ctx_train</c> there),
    /// then falls back to <c>GET /props</c> at the server root.
    /// </summary>
    /// <param name="config">Any OpenAI-compatible config pointing to the llama-server.</param>
    /// <param name="client">Shared HTTP client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Server context props, or an error.</returns>
    public static async Task<Result<LlamaCppServerProps, LlmError>> GetPropsAsync(
        OpenAIProtocolConfig config,
        HttpClient client,
        CancellationToken ct = default)
    {
        // Try /v1/models first — newer llama.cpp includes meta.n_ctx_train in the model entry.
        var modelsResult = await TryGetFromModelsEndpointAsync(config, client, ct);
        if (modelsResult != null) return new Result<LlamaCppServerProps, LlmError>.Ok(modelsResult);

        // Fall back to the native /props endpoint at the server root (no /v1 prefix).
        return await GetFromPropsEndpointAsync(config, client, ct);
    }

    // HELPERS =====================================================================================

    private static async Task<LlamaCppServerProps?> TryGetFromModelsEndpointAsync(
        OpenAIProtocolConfig config,
        HttpClient client,
        CancellationToken ct)
    {
        try
        {
            using var request = CreateAuthedGetRequest($"{config.BaseUrl}/models", config);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                return null;

            var first = data[0];
            if (!first.TryGetProperty("meta", out var meta)) return null;

            if (!meta.TryGetProperty("n_ctx_train", out var ctxTrainEl)
                || !ctxTrainEl.TryGetInt32(out int nCtxTrain)
                || nCtxTrain <= 0)
                return null;

            int nCtx = nCtxTrain;
            if (meta.TryGetProperty("n_ctx", out var ctxEl) && ctxEl.TryGetInt32(out int ctxFromMeta))
                nCtx = ctxFromMeta;

            return new LlamaCppServerProps(nCtxTrain, nCtx);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<Result<LlamaCppServerProps, LlmError>> GetFromPropsEndpointAsync(
        OpenAIProtocolConfig config,
        HttpClient client,
        CancellationToken ct)
    {
        string serverRoot = config.BaseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? config.BaseUrl[..^3]
            : config.BaseUrl;

        try
        {
            using var request = CreateAuthedGetRequest($"{serverRoot}/props", config);
            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return new Result<LlamaCppServerProps, LlmError>.Err(
                    new LlmError(HttpErrorMapper.MapStatusCode(response.StatusCode), body));
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // n_ctx_train is present at the root level in newer llama.cpp builds.
            int nCtxTrain = 0;
            if (root.TryGetProperty("n_ctx_train", out var ctxTrainEl))
                ctxTrainEl.TryGetInt32(out nCtxTrain);

            // n_ctx is in default_generation_settings in all builds.
            int nCtx = 0;
            if (root.TryGetProperty("default_generation_settings", out var settings)
                && settings.TryGetProperty("n_ctx", out var ctxEl))
                ctxEl.TryGetInt32(out nCtx);

            // If n_ctx_train wasn't available, use n_ctx as the best available value.
            if (nCtxTrain <= 0) nCtxTrain = nCtx;

            if (nCtxTrain <= 0 && nCtx <= 0)
            {
                return new Result<LlamaCppServerProps, LlmError>.Err(
                    new LlmError(LlmErrorKind.InvalidRequest, "Server did not return context window information."));
            }

            return new Result<LlamaCppServerProps, LlmError>.Ok(
                new LlamaCppServerProps(nCtxTrain, nCtx));
        }
        catch (OperationCanceledException)
        {
            return new Result<LlamaCppServerProps, LlmError>.Err(
                new LlmError(LlmErrorKind.Cancelled, "Request was cancelled."));
        }
        catch (HttpRequestException ex)
        {
            return new Result<LlamaCppServerProps, LlmError>.Err(
                new LlmError(LlmErrorKind.Network, ex.Message));
        }
    }

    private static HttpRequestMessage CreateAuthedGetRequest(string url, OpenAIProtocolConfig config)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(config.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        return request;
    }
}
