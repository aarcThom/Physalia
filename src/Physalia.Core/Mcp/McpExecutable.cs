// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;

namespace Physalia.Core.Mcp;

/// <summary>
/// Resolves the <c>command</c> of an MCP server entry to a launchable full path.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Practically every published MCP configuration starts
/// <c>"command": "npx"</c> (or <c>uvx</c>, <c>python</c>, <c>docker</c>). On Windows those are
/// <c>.cmd</c> shims, and Windows' <c>CreateProcess</c> — which is what
/// <see cref="System.Diagnostics.Process"/> calls with <c>UseShellExecute = false</c> — does
/// <b>not</b> apply <c>PATHEXT</c>. Verified 2026-08-27: <c>npx</c> throws
/// <c>Win32Exception: The system cannot find the file specified</c>, while the resolved
/// <c>C:\Program Files\nodejs\npx.cmd</c> starts and answers normally. Without this resolver the
/// single most common configuration line in the ecosystem would fail on every Windows machine.</para>
/// <para>Resolving to a full path also fixes the weaker half of the problem: a bare <c>npx.cmd</c>
/// found on <c>PATH</c> did start but exited 1, where the full path exited 0.</para>
/// </remarks>
public static class McpExecutable
{
    /// <summary>
    /// Finds the executable a command name refers to.
    /// </summary>
    /// <param name="command">The <c>command</c> value from the server entry.</param>
    /// <returns>
    /// An absolute path to launch, or null when nothing matching is on <c>PATH</c>. A command that
    /// already contains a directory separator is returned unchanged — the user named a specific file
    /// and second-guessing it would be wrong.
    /// </returns>
    public static string? Resolve(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string trimmed = command.Trim();

        if (trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            return trimmed;
        }

        foreach (string directory in SearchDirectories())
        {
            foreach (string candidate in Candidates(trimmed))
            {
                string full;
                try
                {
                    full = Path.Combine(directory, candidate);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry; skip it rather than failing the whole lookup.
                    continue;
                }

                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Describes what to tell the user when <see cref="Resolve"/> found nothing.
    /// </summary>
    /// <param name="command">The command that could not be found.</param>
    /// <returns>A message naming the command and the likely cause.</returns>
    public static string DescribeMissing(string? command) =>
        $"'{command}' is not on PATH. Install it, or give the full path in the server's `command`." +
        (IsWindows ? " On Windows, node tools are usually at C:\\Program Files\\nodejs\\." : string.Empty);

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static IEnumerable<string> SearchDirectories()
    {
        // The current directory is deliberately NOT searched: launching a stray executable that
        // happens to sit next to a Grasshopper file because a config said "python" would be a
        // genuine security problem, not a convenience.
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (string entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = entry.Trim().Trim('"');
            if (directory.Length > 0)
            {
                yield return directory;
            }
        }
    }

    private static IEnumerable<string> Candidates(string command)
    {
        // An explicit extension is honoured as written and never doubled up.
        if (Path.HasExtension(command))
        {
            yield return command;
            yield break;
        }

        if (!IsWindows)
        {
            yield return command;
            yield break;
        }

        // PATHEXT variants come FIRST on Windows, and the order matters. npm installs both `npx`
        // (a Unix shell script) and `npx.cmd` into the same directory; preferring the bare name
        // picks the shell script, which CreateProcess rejects with "not a valid application for
        // this OS platform". Found live 2026-08-27.
        string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";

        foreach (string extension in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = extension.Trim();
            if (trimmed.StartsWith(".", StringComparison.Ordinal))
            {
                yield return command + trimmed;
            }
        }

        // Last resort: an extensionless native binary really can sit on a Windows PATH.
        yield return command;
    }
}
