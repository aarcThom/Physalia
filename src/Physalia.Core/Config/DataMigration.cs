// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Config;

/// <summary>
/// Carries the user's own files out of an old install directory and into
/// <see cref="PhyData.Root"/>, once.
/// </summary>
/// <remarks>
/// <para><b>Why this has to exist at all.</b> Every build before this one wrote memories, project
/// folders, downloads and saved presets into <c>Files/</c> beside the plug-in. Rhino installs each
/// package version into a directory of its own and updates packages silently at startup, so an
/// update leaves all of it in a folder nothing reads any more — present on disk, invisible to the
/// user, and indistinguishable from data loss. The move happens on the first run of the new build,
/// after which the old folders are empty and this finds nothing to do.</para>
/// <para><b>Every rule here exists to avoid destroying something.</b> An entry is moved only when
/// nothing of that name is at the destination — never merged, never overwritten; a blocked entry is
/// reported and left exactly where it is, for a human to reconcile. Nothing is deleted, ever, even
/// after everything around it has moved: the package directory is not ours to tidy, and the next
/// update replaces it wholesale anyway.</para>
/// <para><b>Planning is separated from doing</b> so the decisions — which entries, from which of
/// several candidate roots, in what order, and which ones are refused — can be tested without a
/// filesystem. <see cref="Plan"/> is pure; <see cref="Run"/> is the only part that touches disk.</para>
/// </remarks>
public static class DataMigration
{
    /// <summary>
    /// The folders whose CONTENTS belong to the user, relative to a <c>Files/</c> root. Shipped
    /// content (<c>SYSTEM_PROMPTS</c>, <c>CLUSTERS</c>, <c>PRESETS/Physalia</c>) is deliberately
    /// absent: it is still read from the package, and the user's data folder merely shadows it.
    /// </summary>
    public static readonly IReadOnlyList<string> UserOwnedFolders = new[]
    {
        PhyData.ProjectFiles,
        PhyData.Memories,
        PhyData.Presets + "/User",
        PhyData.Presets + "/Community",
    };

    // Packaging artifacts, not user data. They are in the repo to keep an empty folder in git and to
    // explain it to whoever opens it, and they are copied into every build — so migrating one would
    // move a file the next update simply puts back, and then report it as blocked forever.
    private static readonly IReadOnlyList<string> Ignored = new[] { ".gitkeep", "README.md" };

    /// <summary>
    /// Works out what to move, without touching anything.
    /// </summary>
    /// <param name="legacyRoots">
    /// <c>Files/</c> roots to scan, in the order they should be preferred — see
    /// <see cref="OrderRoots"/>. A root that does not exist contributes nothing.
    /// </param>
    /// <param name="dataRoot">The user data folder to move into (<see cref="PhyData.Root"/>).</param>
    /// <param name="probe">How to look at the filesystem.</param>
    /// <returns>
    /// One step per entry found, in the order they should be applied, including the entries that are
    /// blocked by something already at the destination.
    /// </returns>
    public static IReadOnlyList<DataMigrationStep> Plan(
        IReadOnlyList<string> legacyRoots,
        string dataRoot,
        IMigrationProbe probe)
    {
        ArgumentNullException.ThrowIfNull(legacyRoots);
        ArgumentNullException.ThrowIfNull(probe);

        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new ArgumentException("A data root is required.", nameof(dataRoot));

        var steps = new List<DataMigrationStep>();

        // Claimed destinations are tracked as the plan is built, not just read off disk: two legacy
        // roots can hold the same harness's project folder, and without this the second one would be
        // planned as a move onto a destination the first step is about to create.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in legacyRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (string folder in UserOwnedFolders)
            {
                string source = Combine(root, folder);
                if (!probe.DirectoryExists(source))
                {
                    continue;
                }

                string target = Combine(dataRoot, folder);

                // Sorted so a plan is reproducible and its log reads in a sensible order; the
                // filesystem's own enumeration order is guaranteed to be neither.
                IEnumerable<string> names = probe.EntryNames(source)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Where(n => !Ignored.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

                foreach (string name in names)
                {
                    string from = Path.Combine(source, name);
                    string to = Path.Combine(target, name);
                    bool blocked = claimed.Contains(to) || probe.EntryExists(to);

                    if (!blocked)
                    {
                        claimed.Add(to);
                    }

                    steps.Add(new DataMigrationStep(folder, name, from, to, blocked));
                }
            }
        }

        return steps;
    }

    /// <summary>
    /// Applies a plan.
    /// </summary>
    /// <param name="steps">The plan, from <see cref="Plan"/>.</param>
    /// <param name="probe">How to move things, and to make the destination folders.</param>
    /// <returns>What happened, for the caller to log.</returns>
    /// <remarks>
    /// A step that fails is reported and skipped — one file held open by a virus scanner must not
    /// stop the other fifty moving. Nothing in here throws: this runs during plug-in load, and a
    /// plug-in that refuses to load because it could not move a memory file is a worse outcome than
    /// the one it is trying to prevent.
    /// </remarks>
    public static DataMigrationReport Run(IReadOnlyList<DataMigrationStep> steps, IMigrationProbe probe)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(probe);

        int moved = 0;
        var blocked = new List<string>();
        var failed = new List<string>();

        foreach (DataMigrationStep step in steps)
        {
            if (step.Blocked)
            {
                blocked.Add(
                    $"{step.Folder}/{step.Name} was left in {step.Source} — something of that name is already in your data folder.");
                continue;
            }

            string? error = probe.Move(step.Source, step.Destination);
            if (error is null)
            {
                moved++;
            }
            else
            {
                failed.Add($"{step.Folder}/{step.Name} could not be moved out of {step.Source}: {error}");
            }
        }

        return new DataMigrationReport(moved, blocked, failed);
    }

    /// <summary>
    /// The <c>Files/</c> roots worth scanning, best first: the one beside this install, then the ones
    /// belonging to other installed versions of the package.
    /// </summary>
    /// <param name="assemblyDirectory">The directory the plug-in was loaded from.</param>
    /// <param name="probe">How to list the sibling directories.</param>
    /// <returns>Candidate roots, most-preferred first. Existence is not checked here.</returns>
    /// <remarks>
    /// The siblings are what makes this work for somebody who updated BEFORE this shipped: Rhino
    /// installs each package version into its own directory, so the previous version's data is one
    /// directory over rather than underneath. Versions sort newest-first when the folder names parse
    /// as versions, which is the layout the package manager produces; anything else falls in behind
    /// them by name.
    /// </remarks>
    public static IReadOnlyList<string> OrderRoots(string assemblyDirectory, IMigrationProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            return Array.Empty<string>();
        }

        string self = assemblyDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var roots = new List<string> { Path.Combine(self, PhyData.PackageFolderName) };

        string? parent = Path.GetDirectoryName(self);
        if (string.IsNullOrEmpty(parent))
        {
            return roots;
        }

        IEnumerable<string> siblings = probe.DirectoryNames(parent)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Where(n => !string.Equals(Path.Combine(parent, n), self, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => Version.TryParse(n, out _) ? 0 : 1)
            .ThenByDescending(n => Version.TryParse(n, out Version? parsed) ? parsed : new Version(0, 0))
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase);

        roots.AddRange(siblings.Select(n => Path.Combine(parent, n, PhyData.PackageFolderName)));
        return roots;
    }

    private static string Combine(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>
/// One entry the migration found.
/// </summary>
/// <param name="Folder">Which user-owned folder it was in, relative to the <c>Files/</c> root.</param>
/// <param name="Name">The entry's name — a harness's project folder, a memory folder, a preset file.</param>
/// <param name="Source">Where it is now.</param>
/// <param name="Destination">Where it belongs.</param>
/// <param name="Blocked">
/// True when something of that name is already at the destination, in which case the entry is
/// reported and left alone rather than merged over.
/// </param>
public sealed record DataMigrationStep(
    string Folder,
    string Name,
    string Source,
    string Destination,
    bool Blocked);

/// <summary>
/// What a migration pass did.
/// </summary>
/// <param name="Moved">How many entries were moved.</param>
/// <param name="Blocked">One message per entry left behind because the destination was taken.</param>
/// <param name="Failed">One message per entry that could not be moved.</param>
public sealed record DataMigrationReport(
    int Moved,
    IReadOnlyList<string> Blocked,
    IReadOnlyList<string> Failed)
{
    /// <summary>
    /// Gets a value indicating whether anything happened worth telling the user about.
    /// </summary>
    /// <remarks>
    /// False is the steady state — every later startup finds the legacy folders empty, or finds only
    /// shipped content the user already has — and is the case where nothing should be written to the
    /// Rhino command line. A blocked entry on its own is not news: a package that ships a demo
    /// project folder blocks it on every update, forever, and saying so each time trains the user to
    /// ignore the line that matters.
    /// </remarks>
    public bool Any => Moved > 0 || Failed.Count > 0;
}

/// <summary>
/// The filesystem, as the migration needs to see it.
/// </summary>
/// <remarks>
/// An interface rather than direct calls, so the planner's rules — which entries, which root wins,
/// what counts as blocked — are testable without writing to a real disk.
/// </remarks>
public interface IMigrationProbe
{
    /// <summary>
    /// Whether a directory exists.
    /// </summary>
    /// <param name="path">The directory.</param>
    /// <returns>True when it is there.</returns>
    bool DirectoryExists(string path);

    /// <summary>
    /// Whether a file OR a directory exists at a path.
    /// </summary>
    /// <param name="path">The entry.</param>
    /// <returns>True when something is there.</returns>
    bool EntryExists(string path);

    /// <summary>
    /// The names of everything directly inside a directory, files and directories alike.
    /// </summary>
    /// <param name="path">The directory to list.</param>
    /// <returns>Entry names, or empty when the directory cannot be read.</returns>
    IReadOnlyList<string> EntryNames(string path);

    /// <summary>
    /// The names of the directories directly inside a directory.
    /// </summary>
    /// <param name="path">The directory to list.</param>
    /// <returns>Directory names, or empty when it cannot be read.</returns>
    IReadOnlyList<string> DirectoryNames(string path);

    /// <summary>
    /// Moves a file or directory, creating the destination's parent first.
    /// </summary>
    /// <param name="source">What to move.</param>
    /// <param name="destination">Where to move it.</param>
    /// <returns>Null on success, or why it failed.</returns>
    string? Move(string source, string destination);
}
