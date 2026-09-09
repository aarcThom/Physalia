// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Config;

/// <summary>
/// The real filesystem, for <see cref="DataMigration"/>.
/// </summary>
/// <remarks>
/// <para>Every read swallows its failure and answers "no" or "nothing": a directory the user cannot
/// list is indistinguishable, for our purposes, from one that is not there, and a migration is not
/// the place to start reporting permissions problems.</para>
/// <para><b>The copy-then-delete fallback is not optional.</b> A plain move fails across volumes with
/// an <see cref="IOException"/>, and this move crosses volumes routinely — the plug-in installs on
/// whichever drive Rhino is on while <c>%LOCALAPPDATA%</c> follows the user profile, and the two are
/// different drives on plenty of workstations. The copy is verified before the source is deleted, and
/// a failure to delete afterwards leaves a duplicate rather than a hole.</para>
/// </remarks>
public sealed class FileSystemMigrationProbe : IMigrationProbe
{
    /// <inheritdoc/>
    public bool DirectoryExists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool EntryExists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EntryNames(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> DirectoryNames(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc/>
    public string? Move(string source, string destination)
    {
        try
        {
            string? parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            bool isDirectory = Directory.Exists(source);

            try
            {
                if (isDirectory)
                {
                    Directory.Move(source, destination);
                }
                else
                {
                    File.Move(source, destination);
                }

                return null;
            }
            catch (IOException)
            {
                // Across volumes, or a directory the OS will not rename in place.
                if (isDirectory)
                {
                    CopyDirectory(source, destination);
                }
                else
                {
                    File.Copy(source, destination, overwrite: false);
                }
            }

            // Only delete once the copy is there to be seen. A half-copied folder that has already
            // eaten its source is the one outcome worse than not migrating at all.
            if (!EntryExists(destination))
            {
                return "the copy did not arrive.";
            }

            try
            {
                if (isDirectory)
                {
                    Directory.Delete(source, recursive: true);
                }
                else
                {
                    File.Delete(source);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The data is safe at the destination; the old copy is merely still there. Reporting
                // this as a failure would send the user looking for files that did arrive.
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ex.Message;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            string name = Path.GetFileName(file);
            if (!string.IsNullOrEmpty(name))
            {
                File.Copy(file, Path.Combine(destination, name), overwrite: false);
            }
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(directory);
            if (!string.IsNullOrEmpty(name))
            {
                CopyDirectory(directory, Path.Combine(destination, name));
            }
        }
    }
}
