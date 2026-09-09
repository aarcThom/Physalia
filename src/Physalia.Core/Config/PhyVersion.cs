// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;

namespace Physalia.Core.Config;

/// <summary>
/// What version of Physalia this is, in the two spellings the plug-in needs: one to show a human and
/// one to compare and record.
/// </summary>
/// <remarks>
/// <para><b>Why two.</b> A .NET assembly version has four parts and a build usually leaves the last
/// two at zero, so the honest identity of a build (<c>1.2.0.0</c>) is not the string anybody wants
/// to read under a logo. The display form trims the trailing zeroes and never invents anything; the
/// full form is what gets written to the install stamp and compared between runs, because it is the
/// one that cannot be ambiguous.</para>
/// <para>The assembly is passed in rather than fetched: this library is merged into the <c>.gha</c>
/// in a shipped build but is a separate DLL in a developer one, and its own version number is not
/// the plug-in's.</para>
/// </remarks>
public static class PhyVersion
{
    /// <summary>
    /// Reads both spellings off an assembly.
    /// </summary>
    /// <param name="assembly">The plug-in's own assembly.</param>
    /// <returns>The version, or <see cref="PhyVersionInfo.Unknown"/> when the assembly has none.</returns>
    public static PhyVersionInfo Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return For(assembly.GetName().Version);
    }

    /// <summary>
    /// Both spellings of a version.
    /// </summary>
    /// <param name="version">The version, or null when it is not known.</param>
    /// <returns>The version, or <see cref="PhyVersionInfo.Unknown"/> for null.</returns>
    public static PhyVersionInfo For(Version? version) =>
        version is null ? PhyVersionInfo.Unknown : new PhyVersionInfo(Display(version), version.ToString());

    /// <summary>
    /// The form to show a human: trailing zero parts dropped, never below two parts.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>
    /// A two-, three- or four-part string — <c>1.0</c>, <c>1.2.3</c>, <c>1.2.3.4</c>. A zero part
    /// with something non-zero after it is kept, because <c>1.0.3</c> is not <c>1.3</c>.
    /// </returns>
    public static string Display(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        int parts = version.Revision > 0 ? 4 : version.Build > 0 ? 3 : 2;
        return version.ToString(parts);
    }
}

/// <summary>
/// A version in its two forms.
/// </summary>
/// <param name="Display">For a human to read: <c>1.2.3</c>.</param>
/// <param name="Full">
/// For recording and comparing: <c>1.2.3.0</c>. Never trimmed, so two builds that differ only in a
/// trailing part are still told apart.
/// </param>
public sealed record PhyVersionInfo(string Display, string Full)
{
    /// <summary>
    /// Gets the stand-in for an assembly with no version. Shown rather than hidden: a build that
    /// cannot say what it is, is itself worth seeing.
    /// </summary>
    public static PhyVersionInfo Unknown { get; } = new("unknown", "0.0.0.0");
}
