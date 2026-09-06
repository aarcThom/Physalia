// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Physalia.Core.Files;

namespace Physalia.Core.Grounding;

/// <summary>
/// Tells the model where this pipeline's own files live, and what is in there.
///
/// <para><b>The absolute path is the load-bearing part.</b> Without it the model can list files and
/// read them, and can do nothing else with them: a Python script in <c>run_rhino_script</c> needs a
/// real path to <c>open</c>, and Rhino's importer needs one to import. Naming the folder is what
/// joins "there is a LiDAR tile in the project folder" to "import it".</para>
///
/// <para>Volatile, because the listing changes the moment anything is downloaded. That keeps it
/// behind the stable sections so it cannot invalidate a provider's cached prefix.</para>
/// </summary>
/// <param name="FolderPath">The absolute project folder, or null when none is resolved.</param>
/// <param name="Files">What is in it, already capped by the reader.</param>
/// <param name="Problem">Why no folder resolved, when none did.</param>
/// <param name="Truncated">True when more files exist than are listed.</param>
public sealed record ProjectFolderGrounding(
    string? FolderPath,
    IReadOnlyList<ProjectFileInfo> Files,
    string? Problem = null,
    bool Truncated = false) : Grounding
{
    // Enough for the model to see what a project holds without a folder of five hundred tiles
    // crowding out everything else in the prompt. The same reasoning as the Rhino grounder's caps:
    // a large project should contribute a section the size of a small one's.
    private const int MaxListed = 40;

    /// <inheritdoc/>
    public override bool IsVolatile => true;

    /// <inheritdoc/>
    public override string ToSystemPromptSection()
    {
        if (string.IsNullOrWhiteSpace(this.FolderPath))
        {
            return string.IsNullOrWhiteSpace(this.Problem)
                ? string.Empty
                : "PROJECT FOLDER: not available — " + this.Problem;
        }

        var section = new StringBuilder();
        section.Append("PROJECT FOLDER — this pipeline's own working files are in:\n");
        section.Append(this.FolderPath);
        section.Append("\n\nThat is a real path on this machine: use it verbatim when a script or a "
            + "Grasshopper component needs to open one of these files. Anything downloaded is saved here, "
            + "and read_file reads from here.\n");

        if (this.Files.Count == 0)
        {
            section.Append("\nThe folder is currently empty.");
            return section.ToString();
        }

        List<ProjectFileInfo> shown = this.Files.Take(MaxListed).ToList();

        section.Append("\nIt currently holds ");
        section.Append(this.Files.Count);
        section.Append(this.Files.Count == 1 ? " file" : " files");
        section.Append(this.Truncated ? " (most recent listed first, and there are more):\n" : ":\n");

        foreach (ProjectFileInfo file in shown)
        {
            section.Append("- ");
            section.Append(file.Path);
            section.Append(" (");
            section.Append(FileDownload.Describe(file.Bytes));
            section.Append(")\n");
        }

        if (this.Files.Count > shown.Count)
        {
            section.Append("- …and ");
            section.Append(this.Files.Count - shown.Count);
            section.Append(" more. Use read_file with action \"list\" for the full set.\n");
        }

        return section.ToString().TrimEnd();
    }
}
