// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Physalia.McpBridge;

/// <summary>
/// Bridges a remote (Streamable HTTP) MCP server onto stdio, so Physalia — which speaks only the
/// stdio transport — can reach it.
/// </summary>
/// <remarks>
/// <para>This is a <b>relay, not a second MCP implementation</b>. Every JSON-RPC message read on
/// stdin is forwarded verbatim to the remote endpoint, and every message coming back is written to
/// stdout. All MCP semantics stay in Physalia; this process exists for two things it cannot do
/// in-Rhino: Streamable HTTP, and the OAuth 2.1 sign-in that protects most hosted servers.</para>
/// <para><b>stdout is the protocol.</b> Every diagnostic goes to stderr; a stray Console.WriteLine
/// here corrupts the stream.</para>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? url = ReadOption(args, "--url");
        if (string.IsNullOrWhiteSpace(url))
        {
            await Console.Error.WriteLineAsync(
                "usage: Physalia.McpBridge --url <endpoint> [--header Name=Value ...] [--scope <scope>]");
            return 2;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? endpoint))
        {
            await Console.Error.WriteLineAsync($"'{url}' is not an absolute URL.");
            return 2;
        }

        using var lifetime = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            lifetime.Cancel();
        };

        try
        {
            return await RelayAsync(endpoint, args, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"bridge failed: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RelayAsync(Uri endpoint, string[] args, CancellationToken ct)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            Name = endpoint.Host,
            OAuth = BuildOAuthOptions(args),
        };

        foreach ((string name, string value) in ReadHeaders(args))
        {
            transportOptions.AdditionalHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            transportOptions.AdditionalHeaders[name] = value;
        }

        await using var clientTransport = new HttpClientTransport(transportOptions);
        await using ITransport transport = await clientTransport.ConnectAsync(ct).ConfigureAwait(false);

        await Console.Error.WriteLineAsync($"bridge connected to {endpoint}").ConfigureAwait(false);

        // No BOM, and an explicit flush per message: the reader on the other end is line-oriented.
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

        Task inbound = PumpStdinAsync(stdin, transport, ct);
        Task outbound = PumpTransportAsync(transport, stdout, ct);

        // Either direction closing ends the session: a half-open relay would leave Physalia waiting
        // on a reply that can never arrive.
        await Task.WhenAny(inbound, outbound).ConfigureAwait(false);
        return 0;
    }

    private static async Task PumpStdinAsync(TextReader stdin, ITransport transport, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line = await stdin.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            JsonRpcMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<JsonRpcMessage>(line, McpJsonUtilities.DefaultOptions);
            }
            catch (JsonException ex)
            {
                await Console.Error.WriteLineAsync($"bridge: unreadable message from host: {ex.Message}").ConfigureAwait(false);
                continue;
            }

            if (message is not null)
            {
                await transport.SendMessageAsync(message, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task PumpTransportAsync(ITransport transport, StreamWriter stdout, CancellationToken ct)
    {
        await foreach (JsonRpcMessage message in transport.MessageReader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            string line = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
            await stdout.WriteLineAsync(line).ConfigureAwait(false);
            await stdout.FlushAsync().ConfigureAwait(false);
        }
    }

    private static ClientOAuthOptions BuildOAuthOptions(string[] args)
    {
        // A loopback listener on an OS-assigned port. Registering a fixed port would collide with
        // whatever else the user is running, and the redirect URI has to match what the listener
        // actually bound to.
        int port = FreeLoopbackPort();
        var redirect = new Uri($"http://127.0.0.1:{port}/callback");

        var options = new ClientOAuthOptions
        {
            RedirectUri = redirect,
            AuthorizationCallbackHandler = (context, token) => CaptureAuthorizationAsync(context, redirect, token),

            // No ClientId is configured, so the SDK registers dynamically (RFC 7591) — which is how
            // a desktop client with no pre-provisioned credentials reaches a hosted server at all.
            DynamicClientRegistration = new DynamicClientRegistrationOptions
            {
                ClientName = "Physalia",
                ClientUri = new Uri("https://github.com/aarcThom/Physalia"),
            },
        };

        string? scope = ReadOption(args, "--scope");
        if (!string.IsNullOrWhiteSpace(scope))
        {
            options.Scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return options;
    }

    // Opens the user's browser at the authorization URL and waits for the provider to redirect back
    // to the loopback listener, handing the SDK the code/state/iss it needs to finish the exchange.
    private static async Task<AuthorizationResult> CaptureAuthorizationAsync(
        AuthorizationCallbackContext context,
        Uri redirect,
        CancellationToken ct)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{redirect.Port}/");
        listener.Start();

        await Console.Error.WriteLineAsync($"bridge: opening a browser to sign in — {context.AuthorizationUri}")
            .ConfigureAwait(false);
        OpenBrowser(context.AuthorizationUri);

        using CancellationTokenRegistration registration = ct.Register(() =>
        {
            try
            {
                listener.Stop();
            }
            catch (ObjectDisposedException)
            {
                // Already shut down.
            }
        });

        HttpListenerContext callback = await listener.GetContextAsync().ConfigureAwait(false);

        System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(
            callback.Request.Url?.Query ?? string.Empty);

        string? code = query["code"];
        string? error = query["error"];

        await WriteBrowserReplyAsync(
            callback,
            error is null
                ? "Physalia is signed in. You can close this tab and go back to Rhino."
                : $"Sign-in failed: {error}. You can close this tab.").ConfigureAwait(false);

        listener.Stop();

        if (error is not null)
        {
            throw new InvalidOperationException($"The authorization server returned '{error}'.");
        }

        return new AuthorizationResult
        {
            Code = code ?? string.Empty,
            State = query["state"],
            Iss = query["iss"],
        };
    }

    private static async Task WriteBrowserReplyAsync(HttpListenerContext context, string message)
    {
        byte[] body = Encoding.UTF8.GetBytes(
            $"<!doctype html><meta charset=\"utf-8\"><title>Physalia</title>" +
            $"<body style=\"font:16px system-ui;padding:3rem;\">{WebUtility.HtmlEncode(message)}</body>");

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        context.Response.Close();
    }

    private static int FreeLoopbackPort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void OpenBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // Not fatal: the URL is on stderr above, so the user can still paste it in themselves.
            Console.Error.WriteLine($"bridge: could not open a browser ({ex.Message}). Open the URL above manually.");
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static IEnumerable<(string Name, string Value)> ReadHeaders(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--header", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string raw = args[i + 1];
            int split = raw.IndexOf('=');
            if (split > 0)
            {
                yield return (raw.Substring(0, split), raw.Substring(split + 1));
            }
        }
    }
}
