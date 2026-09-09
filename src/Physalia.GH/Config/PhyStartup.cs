// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Grasshopper.Kernel;
using Physalia.Core.Config;

namespace Physalia.GH.Config;

/// <summary>
/// What Physalia does once, before any canvas exists: carry the user's own files out of an old
/// install directory, and work out whether this run is the first one after an update.
/// </summary>
/// <remarks>
/// <para>Its own <see cref="GH_AssemblyPriority"/> rather than a line inside the widget's, because
/// the widget's is compiled for Windows only — and a Mac user's memories need moving just as much.
/// Grasshopper does not order priority classes, which is fine: nothing here depends on the other,
/// and nothing READS a project folder or a memory until a component solves.</para>
/// <para>Everything here is wrapped: a plug-in that fails to load because it could not move a file
/// would be a far worse bug than the one it exists to prevent.</para>
/// </remarks>
public sealed class PhyStartup : GH_AssemblyPriority
{
    private static readonly object Gate = new object();
    private static bool _done;

    /// <summary>
    /// Gets the notice to show the user once, when this run is the first after a version change.
    /// Null when there is nothing to say — a first install, an unchanged version, a downgrade, or an
    /// acknowledgement already recorded.
    /// </summary>
    /// <remarks>
    /// Held here rather than pushed anywhere, and cleared by <see cref="AcknowledgeNotice"/> rather
    /// than by delivery: Rhino often starts with no chat window open, and a notice that expires
    /// because nobody was looking is a notice that never happened. The window collects it whenever
    /// it opens.
    /// </remarks>
    internal static UpdateNotice? PendingNotice { get; private set; }

    /// <summary>
    /// Runs the once-per-process startup work.
    /// </summary>
    /// <returns>An instruction telling Grasshopper to continue loading.</returns>
    public override GH_LoadingInstruction PriorityLoad()
    {
        Run();
        return GH_LoadingInstruction.Proceed;
    }

    /// <summary>
    /// Forgets the pending notice, because the user has now actually seen it.
    /// </summary>
    /// <param name="notifyAgain">
    /// False when the user asked not to be told about future updates. The version is still recorded
    /// — they asked to stop being interrupted, not to stop being tracked.
    /// </param>
    internal static void AcknowledgeNotice(bool notifyAgain = true)
    {
        if (PendingNotice is null && notifyAgain)
        {
            return;
        }

        PendingNotice = null;

        try
        {
            InstallStamp.Load(PhyData.Root)
                .Acknowledge(Version.Full, notifyAgain)
                .Save(PhyData.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Gone for this session either way; it may come back on the next start.
        }
    }

    /// <summary>
    /// Gets the running build, read once off the plug-in's own assembly.
    /// </summary>
    internal static PhyVersionInfo Version { get; } = PhyVersion.Of(Assembly.GetExecutingAssembly());

    private static void Run()
    {
        lock (Gate)
        {
            if (_done)
            {
                return;
            }

            _done = true;
        }

        Migrate();
        StampVersion();
    }

    // Records the running version, and notices when it has changed since the last run — which, with
    // Rhino updating packages silently at startup, is the only way an update can be reported at all.
    private static void StampVersion()
    {
        try
        {
            InstallStamp stamp = InstallStamp.Load(PhyData.Root);

            PendingNotice = stamp.NoticeFor(Version) is UpdateNotice notice
                ? notice with { Notes = ReadReleaseNotes(), Folder = PhyData.Root }
                : null;

            InstallStamp updated = stamp.WithVersion(Version);
            if (updated != stamp)
            {
                updated.Save(PhyData.Root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No stamp means no notice, which is worth no noise either.
        }
    }

    // This release's section of the shipped changelog, when it has one. Optional by design: two
    // version numbers say THAT something changed and nothing about what, but a changelog somebody
    // forgot to write must not cost the user the notice itself.
    private static string? ReadReleaseNotes() =>
        ReleaseNotes.SectionFromFile(
            PhyData.PackageFile(Assembly.GetExecutingAssembly(), "CHANGELOG.md"),
            Version.Full);

    // Moves anything a previous build left in its install directory into the user's data folder.
    private static void Migrate()
    {
        try
        {
            string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(assemblyDir))
            {
                return;
            }

            var probe = new FileSystemMigrationProbe();
            IReadOnlyList<string> roots = DataMigration.OrderRoots(assemblyDir, probe);
            DataMigrationReport report = DataMigration.Run(
                DataMigration.Plan(roots, PhyData.Root, probe),
                probe);

            if (!report.Any)
            {
                return;
            }

            Rhino.RhinoApp.WriteLine(
                $"[Physalia] Moved {report.Moved} item(s) of your own work into {PhyData.Root}, where a plug-in update cannot take them away.");

            foreach (string message in report.Blocked)
            {
                Rhino.RhinoApp.WriteLine("[Physalia] " + message);
            }

            foreach (string message in report.Failed)
            {
                Rhino.RhinoApp.WriteLine("[Physalia] " + message);
            }
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine(
                "[Physalia] Could not check for files left by a previous version: " + ex.Message);
        }
    }
}
