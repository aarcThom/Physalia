// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using Grasshopper.Kernel;
using Physalia.Core.Naming;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// The one way a component turns its typed Project Folder value into a directory.
///
/// <para>Four nodes ask this question — the Project Folder grounder, Download File, Read File and
/// Read PDF — and they must all get the same answer, or the model is told about one folder and reads
/// from another. Sharing the resolution is also what lets the folder be configured once, on the
/// grounder, and wired into the rest.</para>
/// </summary>
internal static class ProjectFolderInput
{
    /// <summary>
    /// The wording every node uses for its Project Folder input. Identical everywhere on purpose: the
    /// spellings it describes are a rule about the value, not about the node.
    /// </summary>
    internal const string InputDescription =
        "Where this pipeline's files live. Leave blank for the harness's own folder (named after the "
        + "harness, under Files/PROJECT_FILES) — which is what a Project Folder grounder hands over "
        + "when you wire one in. A plain name is a folder under Files/PROJECT_FILES; anything with a "
        + "slash in it is relative to the saved Grasshopper file; a full path is used as it stands.";

    /// <summary>
    /// Resolves the folder for a component.
    /// </summary>
    /// <param name="component">The node asking, used to find its harness and the host document.</param>
    /// <param name="typed">The node's Project Folder value, which may be blank.</param>
    /// <returns>The resolution, which may carry a problem instead of a path.</returns>
    internal static ProjectPathResolution Resolve(IGH_DocumentObject component, string? typed) =>
        ProjectFolder.Resolve(
            PhyDocuments.Harness(component),
            typed,
            PhyDocuments.Host(component));

    /// <summary>
    /// Resolves the folder and reports any problem on the component, so a misconfigured node says so
    /// on the canvas rather than only failing when the model calls it.
    /// </summary>
    /// <param name="component">The node asking.</param>
    /// <param name="typed">Its Project Folder value.</param>
    /// <returns>The absolute folder, or null when none resolved.</returns>
    internal static string? ResolveOrWarn(GH_Component component, string? typed)
    {
        ProjectPathResolution resolution = Resolve(component, typed);

        if (!resolution.IsResolved)
        {
            component.AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                resolution.ProblemText ?? "No project folder could be resolved.");
            return null;
        }

        return resolution.FullPath;
    }
}
