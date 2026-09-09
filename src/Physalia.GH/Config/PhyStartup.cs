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
/// install directory and into their data folder.
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
    /// Runs the once-per-process startup work.
    /// </summary>
    /// <returns>An instruction telling Grasshopper to continue loading.</returns>
    public override GH_LoadingInstruction PriorityLoad()
    {
        Run();
        return GH_LoadingInstruction.Proceed;
    }

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
    }

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
