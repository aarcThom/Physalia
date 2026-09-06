// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Physalia.Core.Common;
using Physalia.Core.Naming;

namespace Physalia.Core.Files;

/// <summary>
/// Reads files out of a project folder for the model: list them, describe one, read text from one,
/// search inside one.
///
/// <para><b>What this is honestly for.</b> Not the 400MB point cloud a pipeline downloads — nothing
/// good comes of putting that through a language model. It is for everything AROUND it: the metadata
/// JSON, the tile index that says which file covers which block, the CSV of survey points, the readme
/// that explains the naming scheme. Those are small, they are text, and they are what a model needs
/// in order to know which big file to reach for.</para>
///
/// <para><b>The containment guard is not a sandbox, and should not be described as one.</b> Physalia
/// ships <c>run_rhino_script</c>, which runs unrestricted Python in-process and can open any file the
/// user can. Where both are advertised, a model already has the disk. What this guard actually buys
/// is protection against ACCIDENTS — a model guessing at a path because nothing told it where to look
/// — and against cost, since a bounded read cannot put a hundred megabytes into a conversation the
/// way an unbounded <c>print</c> can. Both are real; neither is a security boundary.</para>
///
/// <para>A binary file is reported as what it is rather than returned as text. A LAS file decoded as
/// UTF-8 is a screenful of replacement characters that costs tokens and tells the model nothing, and
/// worse, looks like a file that is merely empty or corrupt.</para>
/// </summary>
public static class FileRead
{
    /// <summary>
    /// The most characters one text read returns unless the caller asks for fewer.
    /// </summary>
    public const int DefaultMaxChars = 8000;

    /// <summary>
    /// The most entries a listing returns.
    /// </summary>
    public const int MaxListed = 500;

    /// <summary>
    /// The most matches a search returns.
    /// </summary>
    public const int MaxMatches = 100;

    /// <summary>
    /// Lists the files in a project folder, newest first.
    /// </summary>
    /// <param name="root">The project folder.</param>
    /// <returns>What is in it, or why it could not be listed.</returns>
    public static Result<IReadOnlyList<ProjectFileInfo>, string> List(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return new Result<IReadOnlyList<ProjectFileInfo>, string>.Err(
                "No project folder is configured.");
        }

        if (!Directory.Exists(root))
        {
            return new Result<IReadOnlyList<ProjectFileInfo>, string>.Ok(Array.Empty<ProjectFileInfo>());
        }

        try
        {
            List<ProjectFileInfo> entries = Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(MaxListed)
                .Select(info => new ProjectFileInfo(
                    Path.GetRelativePath(root, info.FullName).Replace('\\', '/'),
                    info.Length,
                    info.LastWriteTimeUtc))
                .ToList();

            return new Result<IReadOnlyList<ProjectFileInfo>, string>.Ok(entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<IReadOnlyList<ProjectFileInfo>, string>.Err(
                "The project folder could not be listed: " + ex.Message);
        }
    }

    /// <summary>
    /// Describes one file without reading its contents: size, when it changed, and what its leading
    /// bytes say it is.
    /// </summary>
    /// <param name="root">The project folder.</param>
    /// <param name="relativePath">The file, relative to that folder.</param>
    /// <returns>The description, or why it could not be produced.</returns>
    public static Result<FileDescription, string> Stat(string? root, string relativePath)
    {
        if (!TryResolve(root, relativePath, out string full, out string problem))
        {
            return new Result<FileDescription, string>.Err(problem);
        }

        try
        {
            var info = new FileInfo(full);
            FileNature nature = FileSniff.Describe(full);

            return new Result<FileDescription, string>.Ok(new FileDescription(
                Path.GetRelativePath(root!, full).Replace('\\', '/'),
                full,
                info.Length,
                info.LastWriteTimeUtc,
                nature.IsText,
                nature.Format));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<FileDescription, string>.Err("That file could not be read: " + ex.Message);
        }
    }

    /// <summary>
    /// Reads text out of a file.
    /// </summary>
    /// <param name="root">The project folder.</param>
    /// <param name="relativePath">The file, relative to that folder.</param>
    /// <param name="offset">The character to start at.</param>
    /// <param name="maxChars">The most characters to return.</param>
    /// <returns>The text and where it stopped, or why it could not be read.</returns>
    public static Result<FileTextResult, string> ReadText(
        string? root,
        string relativePath,
        int offset = 0,
        int maxChars = DefaultMaxChars)
    {
        if (!TryResolve(root, relativePath, out string full, out string problem))
        {
            return new Result<FileTextResult, string>.Err(problem);
        }

        FileNature nature = FileSniff.Describe(full);
        if (!nature.IsText)
        {
            // Said plainly, with what the file IS, so the model reaches for the right tool instead of
            // concluding the file is empty or broken.
            return new Result<FileTextResult, string>.Err(
                $"\"{relativePath}\" is not a text file — it looks like {nature.Format}. "
                + "Reading it as text would return nothing usable. Use read_file with action \"stat\" for its "
                + "size and format, and work with the file itself (for example through run_rhino_script) rather "
                + "than reading its bytes here.");
        }

        try
        {
            string all = File.ReadAllText(full, Encoding.UTF8);
            int start = Math.Clamp(offset, 0, all.Length);
            int take = Math.Clamp(maxChars <= 0 ? DefaultMaxChars : maxChars, 1, 200_000);
            string slice = all.Substring(start, Math.Min(take, all.Length - start));

            return new Result<FileTextResult, string>.Ok(
                new FileTextResult(slice, start, start + slice.Length, all.Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<FileTextResult, string>.Err("That file could not be read: " + ex.Message);
        }
    }

    /// <summary>
    /// Finds a string in a text file, reporting line numbers.
    /// </summary>
    /// <param name="root">The project folder.</param>
    /// <param name="relativePath">The file, relative to that folder.</param>
    /// <param name="query">What to look for; matched case-insensitively.</param>
    /// <returns>The matches, or why the search could not run.</returns>
    public static Result<IReadOnlyList<FileMatch>, string> Search(string? root, string relativePath, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return new Result<IReadOnlyList<FileMatch>, string>.Err("A non-empty query is required.");
        }

        if (!TryResolve(root, relativePath, out string full, out string problem))
        {
            return new Result<IReadOnlyList<FileMatch>, string>.Err(problem);
        }

        FileNature nature = FileSniff.Describe(full);
        if (!nature.IsText)
        {
            return new Result<IReadOnlyList<FileMatch>, string>.Err(
                $"\"{relativePath}\" is not a text file — it looks like {nature.Format} — so there is nothing to search.");
        }

        try
        {
            var matches = new List<FileMatch>();
            int number = 0;

            foreach (string line in File.ReadLines(full, Encoding.UTF8))
            {
                number++;
                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new FileMatch(number, Trim(line)));
                    if (matches.Count >= MaxMatches)
                    {
                        break;
                    }
                }
            }

            return new Result<IReadOnlyList<FileMatch>, string>.Ok(matches);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<IReadOnlyList<FileMatch>, string>.Err("That file could not be searched: " + ex.Message);
        }
    }

    /// <summary>
    /// Resolves a model-supplied relative path inside the project folder, refusing anything that
    /// lands outside it.
    /// </summary>
    /// <param name="root">The project folder.</param>
    /// <param name="relativePath">The path the model asked for.</param>
    /// <param name="fullPath">The resolved absolute path.</param>
    /// <param name="problem">Why the path was refused.</param>
    /// <returns>True when the file resolves and exists.</returns>
    public static bool TryResolve(string? root, string relativePath, out string fullPath, out string problem)
    {
        fullPath = string.Empty;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(root))
        {
            problem = "No project folder is configured.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            problem = "A path is required.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('\\', '/').TrimStart('/')));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = $"\"{relativePath}\" is not a usable path.";
            return false;
        }

        if (!ProjectPaths.IsContained(root, fullPath))
        {
            problem = $"\"{relativePath}\" is outside the project folder. Only files in the project folder can be read.";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            problem = $"\"{relativePath}\" is not in the project folder. Use action \"list\" to see what is.";
            return false;
        }

        return true;
    }

    private static string Trim(string line) =>
        line.Length <= 300 ? line.TrimEnd() : line.Substring(0, 300).TrimEnd() + "…";
}

/// <summary>
/// One file in a project folder.
/// </summary>
/// <param name="Path">Its path relative to the project folder.</param>
/// <param name="Bytes">Its size.</param>
/// <param name="ModifiedUtc">When it last changed.</param>
public sealed record ProjectFileInfo(string Path, long Bytes, DateTime ModifiedUtc);

/// <summary>
/// What one file is, short of its contents.
/// </summary>
/// <param name="RelativePath">Its path relative to the project folder.</param>
/// <param name="FullPath">Its absolute path, which a script can open.</param>
/// <param name="Bytes">Its size.</param>
/// <param name="ModifiedUtc">When it last changed.</param>
/// <param name="IsText">Whether it can be read as text.</param>
/// <param name="Format">What its leading bytes say it is, or null for ordinary text.</param>
public sealed record FileDescription(
    string RelativePath,
    string FullPath,
    long Bytes,
    DateTime ModifiedUtc,
    bool IsText,
    string? Format);

/// <summary>
/// A slice of a text file.
/// </summary>
/// <param name="Text">The characters read.</param>
/// <param name="Start">Where the slice began.</param>
/// <param name="End">Where it ended.</param>
/// <param name="TotalChars">How long the whole file is, so the model can tell there is more.</param>
public sealed record FileTextResult(string Text, int Start, int End, int TotalChars)
{
    /// <summary>
    /// Gets a value indicating whether there is more text after this slice.
    /// </summary>
    public bool HasMore => this.End < this.TotalChars;
}

/// <summary>
/// One search hit.
/// </summary>
/// <param name="Line">The 1-based line number.</param>
/// <param name="Text">The line, trimmed to a readable length.</param>
public sealed record FileMatch(int Line, string Text);
