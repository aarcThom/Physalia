// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Config;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Emits the endpoint of a local <c>llama-server</c> as a Model API value, so the OpenAI
/// Compatible Model component can talk to it.
/// </summary>
/// <remarks>
/// <para>The counterpart of the Model API component, for the one provider that has no credential to
/// resolve. A local server is <see cref="ProviderAuth.Detected"/>: nothing about it is stored, so
/// there is nothing for <see cref="ModelApiResolver"/> to hand back and nothing for the Model API
/// component's Picker to list. What a local server needs instead is an address, and an address is
/// not a secret — it belongs on the canvas, where it travels with the definition and inside a
/// preset.</para>
/// <para>The key is always empty. A server started with <c>--api-key</c> is a credentialed endpoint
/// like any other: configure it as "Other (OpenAI-compatible)" in the chat window and use the Model
/// API component.</para>
/// <para>No model picker, and none is needed here — <c>llama-server</c> loads exactly one model and
/// ignores the model id in the request body. The picker alongside the OpenAI Compatible Model
/// component still fills itself from the server's <c>/v1/models</c>, which is where the loaded
/// model's name shows up.</para>
/// </remarks>
public class LlamaCppApi : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaCppApi"/> class.
    /// </summary>
    public LlamaCppApi()
        : base(
            "LlamaCpp API",
            "LCppAPI",
            "Points at a llama-server running on this machine. No key, no account — just the address, which you only need to type if you moved it off the default port.",
            "Models")
    {
        // Until this component has an icon sheet entry of its own, borrow its sibling's rather than
        // fall back to the generic brain.
        IconPath = "Physalia.GH.Resources.LlamaCppModelInfo.png";
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("34AB2C8F-224E-447D-A0EA-08FC1F6AC052");

    /// <summary>
    /// Gets the endpoint used when the Base URL input is left empty.
    /// </summary>
    /// <remarks>
    /// From <see cref="ProviderCatalog"/>, so this node, the chat window's Detect probe and the
    /// setup page's prose all name one address.
    /// </remarks>
    private static string DefaultBaseUrl => ProviderCatalog.LocalLlmEndpoint;

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Base URL",
            "U",
            $"Where the server is listening, ending in /v1. Leave empty for {DefaultBaseUrl}; change it only if you started llama-server on another port or on another machine.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[0].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_ModelApi(),
            "Model API",
            "API",
            "The local server's address, as a Model API. Wire it into an OpenAI Compatible Model; the key it carries is empty, because a local server asks for none.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string baseUrl = string.Empty;
        DA.GetData(0, ref baseUrl);

        baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();

        // A URL missing its scheme reaches the provider as a relative address and fails there, with
        // an error about the request rather than about the address that caused it.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Error,
                $"\"{baseUrl}\" is not an http address. Include the scheme and the path — for example {DefaultBaseUrl}.");
            return;
        }

        // Trailing slashes are harmless to a person and doubled separators to a URL builder.
        baseUrl = baseUrl.TrimEnd('/');

        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Remark,
                "llama-server's OpenAI-compatible routes live under /v1. Add it unless something else is proxying the address.");
        }

        DA.SetData(0, new GH_ModelApi(new ModelApi(ProviderCatalog.LocalLlm, baseUrl, string.Empty)));
    }
}
