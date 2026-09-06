// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Physalia.Core.Naming;

/// <summary>
/// How a typed folder value becomes a real directory, and the one place a folder NAME is sanitized.
///
/// <para>Four spellings, told apart by shape rather than by a setting, because a setting for this
/// would be one more thing to get wrong:</para>
/// <code>
/// (blank)                   Files/PROJECT_FILES/&lt;the harness's name&gt;
/// site-survey               Files/PROJECT_FILES/site-survey       — no separator, so it is a NAME
/// ./data  ../shared/las     relative to the folder the .gh file is saved in
/// D:\Projects\x  \\share\y  used verbatim
/// </code>
///
/// <para><b>Document-relative is the spelling that earns its place.</b> Downloads sitting beside the
/// Grasshopper file is what an architect actually wants, and it moves when the project directory
/// moves. It costs one rule to distinguish — a separator means a path, no separator means a name —
/// and that rule is teachable in a sentence. An UNSAVED document cannot resolve one, and that is
/// reported rather than quietly redirected somewhere else: silently writing to a fallback folder is
/// how a user loses track of where their files went.</para>
///
/// <para><b>Sanitizing applies to the name spellings only.</b> A name is reduced to a single safe
/// folder name — separators and invalid characters become dashes, leading and trailing dots and
/// dashes are trimmed — which is what stops <c>..</c> walking out of the root. A rooted or relative
/// path is not sanitized, because it is not a name: it is a location somebody typed on purpose. A
/// four-word default key survives sanitizing untouched, which is why the word list is lower-case
/// letters only.</para>
/// </summary>
public static class ProjectPaths
{
    /// <summary>
    /// The folder under a project folder where a Read PDF node looks for its standing set. PDFs used
    /// to have a library of their own; they are project material like anything else, so they sit
    /// inside the project rather than beside it.
    /// </summary>
    public const string PdfSubfolder = "PDF";

    /// <summary>
    /// Where the download tool records what it fetched, so a package can carry the knowledge of a
    /// 400MB file instead of the file.
    /// </summary>
    public const string DownloadLedgerFile = "downloads.json";

    // Last-resort folder for a name that sanitizes away to nothing — a value of only dots or slashes.
    private const string UnnamedKey = "unnamed";

    /// <summary>
    /// Resolves a typed folder value to an absolute directory.
    /// </summary>
    /// <param name="typed">What the user typed, or null/blank for the default.</param>
    /// <param name="defaultKey">
    /// The folder name to use when nothing was typed — the harness's name, already a legal folder
    /// name when it is a generated four-word key.
    /// </param>
    /// <param name="projectFilesRoot">The <c>Files/PROJECT_FILES</c> directory.</param>
    /// <param name="documentFolder">
    /// The folder the host <c>.gh</c> file is saved in, or null when it has never been saved. Only
    /// consulted for a relative path.
    /// </param>
    /// <returns>The resolution, which may carry a problem instead of a path.</returns>
    public static ProjectPathResolution Resolve(
        string? typed,
        string defaultKey,
        string projectFilesRoot,
        string? documentFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFilesRoot))
        {
            return ProjectPathResolution.Problem("Physalia could not work out where its Files folder is.");
        }

        string value = typed?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            return new ProjectPathResolution(
                Path.Combine(projectFilesRoot, FolderKey(defaultKey)),
                ProjectPathKind.Default,
                null);
        }

        if (IsRootedPath(value))
        {
            return new ProjectPathResolution(value, ProjectPathKind.Rooted, null);
        }

        if (HasSeparator(value))
        {
            if (string.IsNullOrWhiteSpace(documentFolder))
            {
                return ProjectPathResolution.Problem(
                    "\"" + value + "\" is relative to the Grasshopper file, but this document has not been "
                    + "saved yet. Save it, or type a full path or a plain folder name instead.");
            }

            string combined;
            try
            {
                combined = Path.GetFullPath(Path.Combine(documentFolder, value));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return ProjectPathResolution.Problem("\"" + value + "\" is not a usable folder path.");
            }

            return new ProjectPathResolution(combined, ProjectPathKind.DocumentRelative, null);
        }

        return new ProjectPathResolution(
            Path.Combine(projectFilesRoot, FolderKey(value)),
            ProjectPathKind.Named,
            null);
    }

    /// <summary>
    /// Reduces a typed name to a single safe folder name. The only place in Physalia this is done,
    /// so the containment rule is stated once rather than copied per tool.
    /// </summary>
    /// <param name="name">The typed name, which may be null, blank or full of nonsense.</param>
    /// <returns>The folder name used on disk.</returns>
    public static string FolderKey(string? name)
    {
        string sanitized = Sanitize(name ?? string.Empty);
        return sanitized.Length == 0 ? UnnamedKey : sanitized;
    }

    /// <summary>
    /// Determines whether a typed value is a filesystem location rather than a folder name.
    ///
    /// <para>Deliberately stricter than <see cref="Path.IsPathRooted(string)"/>, which calls a
    /// leading slash rooted and would reinterpret a name somebody typed with one.</para>
    /// </summary>
    /// <param name="value">The typed value.</param>
    /// <returns>True when the value is an absolute location.</returns>
    public static bool IsRootedPath(string value) =>
        !string.IsNullOrEmpty(value)
        && ((value.Length >= 2 && value[1] == ':')
            || value.StartsWith(@"\\", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal)
            || value[0] == '/');

    /// <summary>
    /// Determines whether a path is contained by a root — the check every file the model names has to
    /// pass before it is opened or written.
    ///
    /// <para>Compares RESOLVED full paths, because that is the only question worth asking: a name can
    /// climb out by many routes and they all end up somewhere, which is the thing to look at.</para>
    /// </summary>
    /// <param name="root">The folder everything must stay inside.</param>
    /// <param name="candidate">The path to test.</param>
    /// <returns>True when the candidate is the root or sits underneath it.</returns>
    public static bool IsContained(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string fullRoot;
        string fullCandidate;
        try
        {
            fullRoot = Path.GetFullPath(root);
            fullCandidate = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (string.Equals(fullRoot, fullCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        return fullCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSeparator(string value) =>
        value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal);

    // Anything a file name cannot hold — the path separators included, which is what makes this a
    // containment guard and not just a tidy-up — becomes a dash. Dots survive in the middle so "v1.2"
    // stays readable, but are trimmed off the ends, so ".." cannot address a parent and a trailing
    // dot cannot produce a name Windows refuses to create.
    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            builder.Append(invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c);
        }

        return builder.ToString().Trim('-', '.');
    }
}

/// <summary>
/// Which spelling a typed folder value turned out to be. Reported so a node can say what it did with
/// what the user typed, rather than leaving them to infer it from where files appear.
/// </summary>
public enum ProjectPathKind
{
    /// <summary>Nothing was typed: the harness's own project folder.</summary>
    Default,

    /// <summary>A plain name: a folder under <c>Files/PROJECT_FILES</c>.</summary>
    Named,

    /// <summary>A relative path: resolved against the saved Grasshopper file's folder.</summary>
    DocumentRelative,

    /// <summary>A full path, used as it stands.</summary>
    Rooted,

    /// <summary>Nothing could be resolved; see the problem text.</summary>
    Unresolvable,
}

/// <summary>
/// The outcome of resolving a typed folder value.
/// </summary>
/// <param name="FullPath">The absolute directory, or null when it could not be resolved.</param>
/// <param name="Kind">Which spelling was recognised.</param>
/// <param name="ProblemText">Why nothing could be resolved, or null when it could.</param>
public sealed record ProjectPathResolution(string? FullPath, ProjectPathKind Kind, string? ProblemText)
{
    /// <summary>
    /// Gets a value indicating whether a directory was resolved.
    /// </summary>
    public bool IsResolved => this.FullPath is { Length: > 0 };

    /// <summary>
    /// Builds an unresolvable result.
    /// </summary>
    /// <param name="text">What to tell the user.</param>
    /// <returns>The resolution.</returns>
    public static ProjectPathResolution Problem(string text) =>
        new(null, ProjectPathKind.Unresolvable, text);
}
