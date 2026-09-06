// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Grasshopper.Kernel;

namespace Physalia.GH.Components;

/// <summary>
/// The "Open Project Folder" item every node that reads or writes project files carries.
///
/// <para>It exists because of the download a program cannot make. A host behind a bot challenge is a
/// door that only opens for a browser, so the recovery is always the same shape: the person fetches
/// the file themselves and puts it where the pipeline is looking. That only works if getting to the
/// folder is trivial — an absolute path in a chat message is correct and is still a path somebody has
/// to copy, paste and navigate. One menu item removes that.</para>
///
/// <para>Useful well beyond the blocked case: dropping in a survey nobody can re-fetch, or checking
/// what a download actually produced, both start here.</para>
/// </summary>
internal static class ProjectFolderMenu
{
    /// <summary>
    /// Appends the item to a component's right-click menu.
    /// </summary>
    /// <param name="component">The node whose menu it is.</param>
    /// <param name="menu">The menu being built.</param>
    /// <param name="folder">
    /// The resolved project folder, or null when the node has not resolved one — in which case the
    /// item is shown disabled rather than hidden, so the absence is visible and reads as "this node
    /// is not configured" rather than as a missing feature.
    /// </param>
    internal static void Append(GH_Component component, ToolStripDropDown menu, string? folder)
    {
        ArgumentNullException.ThrowIfNull(component);

        ToolStripMenuItem item = GH_DocumentObject.Menu_AppendItem(
            menu,
            "Open Project Folder",
            (_, _) => Open(folder));

        item.Enabled = !string.IsNullOrWhiteSpace(folder);
        item.ToolTipText = string.IsNullOrWhiteSpace(folder)
            ? "No project folder is configured on this node."
            : folder;
    }

    /// <summary>
    /// Shows a folder in the system file browser, creating it first if it is not there yet.
    ///
    /// <para>Created rather than refused: the folder is where the user is being asked to save
    /// something, and a node that has resolved a path but never written to it has no folder on disk
    /// yet. Opening nothing would look like a broken menu item at the exact moment it matters.</para>
    /// </summary>
    /// <param name="folder">The folder to show.</param>
    internal static void Open(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);

            // UseShellExecute so the platform's own file browser handles it — Explorer on Windows,
            // Finder through `open` on macOS.
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Rhino.RhinoApp.WriteLine($"[Physalia] Could not open {folder}: {ex.Message}");
        }
    }
}
