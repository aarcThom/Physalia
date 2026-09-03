// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Physalia.Core.Mcp;

/// <summary>
/// One entry from the user's <c>mcpServers</c> configuration: how to reach a single MCP server.
/// Either a local subprocess (<see cref="Command"/>) or a remote endpoint (<see cref="Url"/>).
/// </summary>
/// <param name="Name">
/// The key the entry appeared under, e.g. <c>filesystem</c>. Used to namespace the server's tool
/// names and to label the node that picked it.
/// </param>
/// <param name="Command">
/// Executable to launch for a local stdio server, or null for a remote one.
/// </param>
/// <param name="Arguments">
/// Arguments passed to <see cref="Command"/>, in order. Empty for a remote server.
/// </param>
/// <param name="Environment">
/// Environment variables overlaid on the inherited environment. This is where server credentials
/// live, which is the reason the whole file sits under <c>Files/</c> and never on a component: a
/// definition serialized into a component would ship inside a preset.
/// </param>
/// <param name="WorkingDirectory">
/// Working directory for a local server, or null to use the plug-in's own.
/// </param>
/// <param name="Url">
/// Endpoint of a remote (Streamable HTTP) server, or null for a local one. Physalia speaks only the
/// stdio transport in-process, so a remote entry is reached by launching the Physalia MCP bridge
/// pointed at this URL.
/// </param>
/// <param name="Headers">
/// Extra HTTP headers sent to a remote server, which is where a static bearer token goes. Ignored
/// for a local entry, whose credentials belong in <see cref="Environment"/> instead. Most hosted
/// servers need none of this: the bridge signs in over OAuth, so the usual case is an empty set.
/// </param>
/// <param name="Scope">
/// OAuth scopes requested when signing in to a remote server, space-separated, or null to let the
/// server's own metadata decide. Ignored for a local entry.
/// </param>
public sealed record McpServerDefinition(
    string Name,
    string? Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string? WorkingDirectory,
    string? Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Scope)
{
    /// <summary>
    /// Gets a value indicating whether this entry names a remote endpoint rather than a local
    /// subprocess.
    /// </summary>
    public bool IsRemote => !string.IsNullOrWhiteSpace(Url);

    /// <summary>
    /// Gets a value indicating whether this entry carries enough information to launch anything.
    /// </summary>
    public bool IsRunnable => IsRemote || !string.IsNullOrWhiteSpace(Command);

    /// <summary>
    /// Gets a stable key identifying the process this definition would produce. Two nodes naming
    /// the same server share one connection; changing any launch detail yields a different key and
    /// therefore a fresh process.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes <see cref="Name"/>: two entries differing only in their key are the
    /// same server and should not be launched twice. Environment values and headers are folded in
    /// because a changed token must not be served from a warm process that authenticated with the
    /// old one.
    /// </remarks>
    public string Identity
    {
        get
        {
            // Unit separator: cannot occur in a command, path, or environment value, so two
            // different definitions can never collide by concatenation.
            const char Sep = '\u001f';
            const string SepText = "\u001f";

            var builder = new StringBuilder();
            builder.Append(Url ?? string.Empty).Append(Sep);
            builder.Append(Command ?? string.Empty).Append(Sep);
            builder.Append(string.Join(SepText, Arguments)).Append(Sep);
            builder.Append(WorkingDirectory ?? string.Empty).Append(Sep);

            foreach (KeyValuePair<string, string> pair in Environment.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append(Sep);
            }

            // Folded in for the same reason as the environment: a header is usually a credential,
            // and a warm process authenticated with the old one must not serve the new definition.
            foreach (KeyValuePair<string, string> pair in Headers.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                builder.Append(pair.Key).Append(':').Append(pair.Value).Append(Sep);
            }

            builder.Append(Scope ?? string.Empty).Append(Sep);

            return builder.ToString();
        }
    }

    /// <summary>
    /// Creates an empty definition carrying only a name, for an entry the parser could not complete.
    /// </summary>
    /// <param name="name">The entry key.</param>
    /// <returns>A definition that reports <see cref="IsRunnable"/> false.</returns>
    public static McpServerDefinition Unrunnable(string name) => new(
        name,
        Command: null,
        Arguments: Array.Empty<string>(),
        Environment: new Dictionary<string, string>(StringComparer.Ordinal),
        WorkingDirectory: null,
        Url: null,
        Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Scope: null);
}
