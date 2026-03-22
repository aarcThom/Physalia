// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Physalia.GH;

/// <summary>
/// Provides assembly-level metadata for the Physalia Grasshopper plugin.
/// </summary>
public class Physalia_GHInfo : GH_AssemblyInfo
{
    /// <summary>
    /// Gets the display name of this Grasshopper plugin assembly.
    /// </summary>
    public override string Name => "Physalia";

    /// <summary>
    /// Gets a 24x24 pixel bitmap icon representing this plugin assembly.
    /// </summary>
    public override Bitmap Icon => null;

    /// <summary>
    /// Gets a short description of the purpose of this plugin assembly.
    /// </summary>
    public override string Description => "An open-source LLM powered GH library.";

    /// <summary>
    /// Gets the unique identifier for this plugin assembly.
    /// </summary>
    public override Guid Id => new ("862C53A2-69A1-4B56-A133-26E0BCEDE789");

    /// <summary>
    /// Gets the name of the author or organisation responsible for this plugin.
    /// </summary>
    public override string AuthorName => "Thomas Gaudin";

    /// <summary>
    /// Gets the preferred contact details for the plugin author.
    /// </summary>
    public override string AuthorContact => "thomas@aarc.io";

    /// <summary>
    /// Gets the version string of the plugin, derived from the assembly version.
    /// </summary>
    public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
}
