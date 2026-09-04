// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Net;
using System.Linq;
using System.Net.Http;
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
    // Whether any server message ever reached stdout. A relay that ends having pumped nothing is a
    // failure however cleanly the tasks completed, and that distinction is the only thing standing
    // between "closed the connection" and a usable diagnosis.
    private static bool _relayedAnything;


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
        List<(string Name, string Value)> headers = ReadHeaders(args).ToList();

        // A server given an Authorization header of its own is ALREADY authenticated, and asking the
        // SDK for OAuth as well does not merely add a fallback — it BREAKS the header. Configuring
        // OAuth installs ClientOAuthProvider as a delegating handler in front of every request, and
        // that handler owns the Authorization header: with no token cached it sends the request
        // unauthenticated, so the static credential in AdditionalHeaders never reaches the server.
        // Against Illustrator that produced a 401, then an OAuth discovery attempt the server
        // answers 404, and finally "POST response completed without a reply" — three failures deep,
        // none of them mentioning the header that was quietly dropped at step one.
        bool hasStaticAuthorization = headers.Any(
            h => h.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase));

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            Name = endpoint.Host,
            OAuth = hasStaticAuthorization ? null : BuildOAuthOptions(endpoint, args),

            // The standalone GET stream is how a server pushes messages the client did not ask for,
            // and the spec makes it OPTIONAL — a server with nothing to push answers 405. The SDK
            // ends the session when that GET fails, so a server which declines it in any other way
            // takes the whole connection down with it: Adobe Illustrator answers 404 with
            // {"error":"Not found. Use POST /v1/mcp"}, and the relay died the instant it connected,
            // silently, before a single message crossed. Probed rather than configured, because the
            // user cannot be expected to know which kind of server they were handed — and left ON
            // when the probe cannot tell, since a server that does offer the stream needs it for
            // notifications.
            EnableStandaloneGetStream = await OffersGetStreamAsync(endpoint, headers, ct)
                .ConfigureAwait(false),
        };

        foreach ((string name, string value) in headers)
        {
            transportOptions.AdditionalHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            transportOptions.AdditionalHeaders[name] = value;
        }

        // --trace logs the whole HTTP exchange to stderr. A relay has no other way to explain
        // itself: everything it does happens between two pipes, and "the server closed the
        // connection" is all Physalia can otherwise report.
        bool trace = args.Any(a => a.Equals("--trace", StringComparison.OrdinalIgnoreCase));

        // WE SUPPLY THE HttpClient WHENEVER OAUTH IS NOT IN PLAY, and it is not cosmetic.
        //
        // Adobe Illustrator's MCP server answers every response with `Connection: close`. Left to
        // build its own client, the SDK gets .NET's SocketsHttpHandler, which retries a request
        // whose connection died before any response byte arrived — and against this server the
        // retried body lands on a socket the server is still reading, so it sees TWO JSON objects in
        // one request and rejects the pair:
        //
        //   {"error":{"code":-32700,"message":"JSON parse error: ... at line 2, column 1:
        //    unexpected '{'; expected end of input"},"id":null}
        //
        // which the transport reports as "POST response completed without a reply to request with
        // ID: 1" — a message that names neither the retry nor the server's complaint. A plain
        // HttpClientHandler does not do this, and the exchange succeeds. Measured both ways against
        // the live server.
        //
        // The OAuth path keeps the SDK's own client, because ClientOAuthProvider is installed INTO
        // the client the SDK builds; handing it one of ours would leave the handler out and break
        // the browser sign-in, which this change has no evidence about either way. A server needing
        // OAuth is not one carrying a static Authorization header, so the two cases do not overlap.
        HttpClient? ownClient = transportOptions.OAuth is not null && !trace
            ? null
            : new HttpClient(trace
                ? new StderrTraceHandler(new ContentLengthHandler())
                : new ContentLengthHandler());

        await using var clientTransport = ownClient is null
            ? new HttpClientTransport(transportOptions)
            : new HttpClientTransport(transportOptions, ownClient, null!, ownsHttpClient: true);
        await using ITransport transport = await clientTransport.ConnectAsync(ct).ConfigureAwait(false);

        await Console.Error.WriteLineAsync(
            $"bridge connected to {endpoint} (auth: "
            + $"{(hasStaticAuthorization ? "static header" : "OAuth")}, standalone GET stream: "
            + $"{(transportOptions.EnableStandaloneGetStream ? "yes" : "no")})").ConfigureAwait(false);

        // No BOM, and an explicit flush per message: the reader on the other end is line-oriented.
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

        Task inbound = PumpStdinAsync(stdin, transport, ct);
        Task outbound = PumpTransportAsync(transport, stdout, ct);

        // Either direction closing ends the session: a half-open relay would leave Physalia waiting
        // on a reply that can never arrive.
        Task finished = await Task.WhenAny(inbound, outbound).ConfigureAwait(false);

        // SAY WHICH SIDE WENT, AND WHY. This used to `return 0` unconditionally, so a transport that
        // died before relaying anything looked exactly like a clean shutdown: Physalia could only
        // report "the server closed the connection" and echo whatever stderr happened to hold. An
        // exit code and one line of diagnosis cost nothing and are the difference between a
        // five-minute fix and an afternoon.
        string side = ReferenceEquals(finished, inbound) ? "host" : "server";

        if (finished.IsFaulted)
        {
            Exception? fault = finished.Exception?.GetBaseException();
            await Console.Error.WriteLineAsync(
                $"bridge: the {side} side failed: {fault?.GetType().Name}: {fault?.Message}")
                .ConfigureAwait(false);
            return 1;
        }

        if (!_relayedAnything)
        {
            await Console.Error.WriteLineAsync(
                $"bridge: the {side} side ended before any message was relayed. The endpoint "
                + "answered the handshake but the message stream closed immediately, which is what "
                + "a server refusing the standalone GET stream with something other than 405 looks "
                + "like.").ConfigureAwait(false);
            return 1;
        }

        await Console.Error.WriteLineAsync($"bridge: the {side} side closed the relay.")
            .ConfigureAwait(false);
        return 0;
    }

    // Sends the request body with a Content-Length instead of chunked.
    //
    // THIS IS WHAT MAKES ILLUSTRATOR WORK, and the diagnosis took a while because the symptom named
    // nothing relevant. The SDK hands HttpClient a content whose length it does not know, so .NET
    // frames the POST with `Transfer-Encoding: chunked` — and Adobe Illustrator's server cannot read
    // a chunked request body. It parses the raw framing AS the body, which is why it answered:
    //
    //   -32700 JSON parse error: at line 2, column 1: unexpected '{'; expected end of input
    //
    // The hex chunk-size on line 1 is itself a valid JSON number, so the server read that as the
    // whole message and then found an object on line 2 it had no use for. The transport turned all
    // of that into "POST response completed without a reply to request with ID: 1", which sounds
    // like silence from a server that was in fact complaining loudly.
    //
    // LoadIntoBufferAsync gives the content a known length, so the request goes out with a
    // Content-Length and the server reads it. The cost is holding one JSON-RPC message in memory,
    // which the relay does anyway.
    private sealed class ContentLengthHandler : DelegatingHandler
    {
        public ContentLengthHandler()
            : base(new SocketsHttpHandler())
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    // Logs each request and response to stderr, headers included but credentials never. Used only
    // under --trace.
    private sealed class StderrTraceHandler : DelegatingHandler
    {
        public StderrTraceHandler(HttpMessageHandler inner)
            : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Console.Error.WriteLineAsync($"trace >>> {request.Method} {request.RequestUri}")
                .ConfigureAwait(false);

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                string value = header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    ? "<redacted>"
                    : string.Join(", ", header.Value);
                await Console.Error.WriteLineAsync($"trace >>> {header.Key}: {value}").ConfigureAwait(false);
            }

            if (request.Content is not null)
            {
                await Console.Error.WriteLineAsync(
                    "trace >>> " + await request.Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false)).ConfigureAwait(false);
            }

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            await Console.Error.WriteLineAsync($"trace <<< {(int)response.StatusCode} {response.ReasonPhrase}")
                .ConfigureAwait(false);

            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                await Console.Error.WriteLineAsync(
                    $"trace <<< {header.Key}: {string.Join(", ", header.Value)}").ConfigureAwait(false);
            }

            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                await Console.Error.WriteLineAsync(
                    $"trace <<< {header.Key}: {string.Join(", ", header.Value)}").ConfigureAwait(false);
            }

            // Buffered, so reading it here does not consume the stream the SDK still needs.
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            await Console.Error.WriteLineAsync("trace <<< " + Encoding.UTF8.GetString(body)).ConfigureAwait(false);

            var replacement = new ByteArrayContent(body);
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = replacement;
            return response;
        }
    }

    // True when the endpoint serves the optional standalone GET stream. Anything that is not an
    // event-stream 200 counts as "does not offer it": 405 is the spec's answer, 404 is Illustrator's,
    // and a server that simply has nothing to push may say either. A probe that cannot reach the
    // endpoint at all reports TRUE, leaving the SDK's own behaviour untouched — the connect attempt
    // that follows will produce a far better error than a guess made here.
    private static async Task<bool> OffersGetStreamAsync(
        Uri endpoint,
        List<(string Name, string Value)> headers,
        CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

            foreach ((string name, string value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using HttpResponseMessage response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await Console.Error.WriteLineAsync(
                    $"bridge: GET {endpoint} answered {(int)response.StatusCode}, so the standalone "
                    + "stream is off for this session.").ConfigureAwait(false);
                return false;
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            return string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return true;
        }
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
            _relayedAnything = true;
            string line = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
            await stdout.WriteLineAsync(line).ConfigureAwait(false);
            await stdout.FlushAsync().ConfigureAwait(false);
        }
    }

    private static ClientOAuthOptions BuildOAuthOptions(Uri endpoint, string[] args)
    {
        // A loopback listener on an OS-assigned port. Registering a fixed port would collide with
        // whatever else the user is running, and the redirect URI has to match what the listener
        // actually bound to.
        int port = FreeLoopbackPort();
        var redirect = new Uri($"http://127.0.0.1:{port}/callback");

        string? scope = ReadOption(args, "--scope");

        var options = new ClientOAuthOptions
        {
            RedirectUri = redirect,
            AuthorizationCallbackHandler = (context, token) => CaptureAuthorizationAsync(context, redirect, token),

            // Tokens outlive this process. Without a durable cache the SDK keeps them with the
            // transport, and the bridge is short-lived by design (the connection pool reaps an idle
            // session after ten minutes, and every Rhino restart kills the lot) — so the user would
            // face a browser sign-in on nearly every cold start.
            TokenCache = new FileTokenCache(endpoint, scope),

            // No ClientId is configured, so the SDK registers dynamically (RFC 7591) — which is how
            // a desktop client with no pre-provisioned credentials reaches a hosted server at all.
            DynamicClientRegistration = new DynamicClientRegistrationOptions
            {
                ClientName = "Physalia",
                ClientUri = new Uri("https://github.com/aarcThom/Physalia"),
            },
        };

        if (!string.IsNullOrWhiteSpace(scope))
        {
            options.Scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return options;
    }

    // Opens the user's browser at the authorization URL and waits for the provider to redirect back
    // to the loopback listener, handing the SDK the code/state/iss it needs to finish the exchange.
    private static async Task<AuthorizationResult?> CaptureAuthorizationAsync(
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
