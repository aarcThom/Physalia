// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;
using Physalia.Core.Config.Secrets;

namespace Physalia.Core.Config;

/// <summary>
/// Where Physalia's folders live: the ONE place that decides whether a folder belongs to the USER or
/// to the installed package.
/// </summary>
/// <remarks>
/// <para><b>Why the split exists.</b> Rhino 8 updates packages installed through its Package Manager
/// silently, at startup, with no prompt — and every package version is installed into a directory of
/// its own (<c>…\packages\8.0\Physalia\&lt;version&gt;\</c>). Anything Physalia had written beside its
/// own assembly was therefore not carried forward by an update: a user's memories, project folders,
/// downloads, autosaved transcripts and saved presets were all one silent update away from being
/// stranded in a folder nothing looks in any more. The developer build loses the same data on every
/// rebuild, because <c>CopyLibraryFiles</c> wipes <c>$(TargetDir)Files</c> before staging.</para>
/// <para><b>The rule.</b> Anything the USER or the PIPELINE writes lives under
/// <see cref="Root"/> — the same per-user folder the credential store, the MCP server list and the
/// API endpoint store already use. Anything WE ship stays in the package, read-only, and is replaced
/// wholesale by the next update.</para>
/// <para><b>Where the two meet</b> (system prompts, clusters, presets) the user folder is searched
/// FIRST and shadows the shipped copy of the same name — see <see cref="SearchPath"/>. That is
/// deliberately an overlay rather than a copy-on-first-run: seeding would force a choice between
/// overwriting a file the user has edited and never delivering a fix to a shipped one, and there is
/// no answer to that choice which is right in both directions.</para>
/// </remarks>
public static class PhyData
{
    /// <summary>
    /// One folder per harness: downloads, site data, PDFs, the autosaved transcript, the run log.
    /// </summary>
    public const string ProjectFiles = "PROJECT_FILES";

    /// <summary>
    /// The memory tool's <c>GLOBAL</c> and <c>LOCAL</c> stores.
    /// </summary>
    public const string Memories = "MEMORIES";

    /// <summary>
    /// The preset library. Shipped pipelines come from the package; the user's own and the
    /// community's come from the data folder.
    /// </summary>
    public const string Presets = "PRESETS";

    /// <summary>
    /// Preamble and schema text for the System Prompt component.
    /// </summary>
    public const string SystemPrompts = "SYSTEM_PROMPTS";

    /// <summary>
    /// Cluster files plus the <c>clusters.json</c> manifest that describes them.
    /// </summary>
    public const string Clusters = "CLUSTERS";

    /// <summary>
    /// The folder shipped content sits in, beside the plug-in assembly.
    /// </summary>
    public const string PackageFolderName = "Files";

    private static string? _root;

    /// <summary>
    /// Gets Physalia's per-user data folder — <c>%LOCALAPPDATA%/Physalia</c> on Windows — creating it
    /// if needed.
    /// </summary>
    /// <remarks>
    /// <para>The same root as the credential store, on purpose: one folder to back up, one folder to
    /// clear, and one folder a support question can ask about.</para>
    /// <para>Resolved once. It is asked for inside solves now — a project folder is resolved every
    /// time a node that reads or writes one solves — and the underlying call creates the directory,
    /// which is not something to do several times a second. Every writer still creates its own
    /// target, so a folder deleted mid-session is remade by whatever next writes to it.</para>
    /// </remarks>
    public static string Root => _root ??= SecretStores.DataFolder();

    /// <summary>
    /// Gets the user's project-files root.
    /// </summary>
    public static string ProjectFilesRoot => UserFolder(ProjectFiles);

    /// <summary>
    /// Gets the user's memories root.
    /// </summary>
    public static string MemoriesRoot => UserFolder(Memories);

    /// <summary>
    /// Gets the user's preset root — where "Save Harness as Preset…" writes.
    /// </summary>
    public static string PresetsRoot => UserFolder(Presets);

    /// <summary>
    /// Gets the user's system-prompts root, which shadows the shipped one file for file.
    /// </summary>
    public static string SystemPromptsRoot => UserFolder(SystemPrompts);

    /// <summary>
    /// Gets the user's clusters root, which shadows the shipped one file for file.
    /// </summary>
    public static string ClustersRoot => UserFolder(Clusters);

    /// <summary>
    /// Resolves a named folder inside the user's data folder. Not created — a reader checks for
    /// itself, and a writer creates only what it is about to write to.
    /// </summary>
    /// <param name="folder">The folder name, one of the constants on this class.</param>
    /// <returns>The absolute path.</returns>
    public static string UserFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("A folder name is required.", nameof(folder));

        return Path.Combine(Root, folder);
    }

    /// <summary>
    /// Resolves the SHIPPED content folder beside an assembly — the <c>Files/</c> tree itself.
    /// </summary>
    /// <param name="assembly">The plug-in's own assembly.</param>
    /// <returns>The absolute path, or null when the assembly has no location on disk.</returns>
    public static string? PackageRoot(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string? assemblyDir = Path.GetDirectoryName(assembly.Location);
        return string.IsNullOrEmpty(assemblyDir)
            ? null
            : Path.Combine(assemblyDir, PackageFolderName);
    }

    /// <summary>
    /// Resolves a file shipped in the package's <c>Files/</c> root, such as the changelog.
    /// </summary>
    /// <param name="assembly">The plug-in's own assembly.</param>
    /// <param name="fileName">The file's name.</param>
    /// <returns>The absolute path, or null when the assembly has no location on disk.</returns>
    public static string? PackageFile(Assembly assembly, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A file name is required.", nameof(fileName));

        string? root = PackageRoot(assembly);
        return root is null ? null : Path.Combine(root, fileName);
    }

    /// <summary>
    /// Resolves a named folder in the SHIPPED content beside an assembly.
    /// </summary>
    /// <param name="assembly">
    /// The assembly whose install directory holds <c>Files/</c>. Pass the calling plug-in's own
    /// assembly; nothing in this library may assume it was loaded from the same place.
    /// </param>
    /// <param name="folder">The folder name, one of the constants on this class.</param>
    /// <returns>
    /// The absolute path, or null when the assembly has no location on disk — which is what a
    /// single-file publish looks like, and is not an error.
    /// </returns>
    public static string? PackageFolder(Assembly assembly, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("A folder name is required.", nameof(folder));

        string? root = PackageRoot(assembly);
        return root is null ? null : Path.Combine(root, folder);
    }

    /// <summary>
    /// The folders to search for shipped-or-overridden content, in precedence order: the user's copy
    /// first, then what we ship.
    /// </summary>
    /// <param name="assembly">The plug-in's own assembly (see <see cref="PackageFolder"/>).</param>
    /// <param name="folder">The folder name, one of the constants on this class.</param>
    /// <returns>
    /// One or two absolute paths, highest precedence first. Neither is guaranteed to exist; callers
    /// take the first hit and skip what is not there.
    /// </returns>
    public static IReadOnlyList<string> SearchPath(Assembly assembly, string folder)
    {
        string user = UserFolder(folder);
        string? package = PackageFolder(assembly, folder);

        return package is null || string.Equals(package, user, StringComparison.OrdinalIgnoreCase)
            ? new[] { user }
            : new[] { user, package };
    }
}
