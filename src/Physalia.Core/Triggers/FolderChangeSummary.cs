// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text;

namespace Physalia.Core.Triggers;

/// <summary>
/// Turns a burst of file-system events into the one sentence a pipeline should be woken with.
///
/// <para><b>Coalescing is the whole job.</b> A single copied folder raises one event per file, and a
/// single SAVED file raises several for that one file — the operating system reports the write, the
/// timestamp and often the size separately. Firing per event would start a round per event; reporting
/// the raw list would tell the model a file changed three times. So events are folded per path first,
/// and the folding rules are not "last one wins": a file that was <em>added</em> and then written to
/// is still an addition (the write is the copy finishing), and a file that appeared and then vanished
/// inside one window is not reported at all, because that is a temporary file and nothing downstream
/// can act on it.</para>
/// </summary>
public static class FolderChangeSummary
{
    /// <summary>
    /// How many individual files a summary names before it starts counting instead.
    /// </summary>
    public const int DefaultMaxListed = 25;

    /// <summary>
    /// Folds a burst of raw events into one entry per file, in the order the files were first seen.
    /// </summary>
    /// <param name="changes">The raw events, oldest first.</param>
    /// <returns>One change per path, with events that cancel out removed entirely.</returns>
    public static IReadOnlyList<FolderChange> Coalesce(IEnumerable<FolderChange>? changes)
    {
        if (changes is null)
        {
            return Array.Empty<FolderChange>();
        }

        // Insertion-ordered: a dictionary for the fold, a list for the order. The order matters
        // because the summary is read by a person as well as a model, and "the order things
        // happened" is the only ordering either of them will expect.
        var folded = new Dictionary<string, FolderChangeKind>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FolderChange change in changes)
        {
            if (change is null || string.IsNullOrWhiteSpace(change.RelativePath))
            {
                continue;
            }

            string path = Normalise(change.RelativePath);

            if (!folded.TryGetValue(path, out FolderChangeKind existing))
            {
                folded[path] = change.Kind;
                order.Add(path);
                continue;
            }

            if (existing == FolderChangeKind.Added && change.Kind == FolderChangeKind.Removed)
            {
                // Appeared and went inside one window: a temporary file. Reporting it would wake the
                // pipeline for something that is no longer there to be read.
                dropped.Add(path);
                folded.Remove(path);
                continue;
            }

            folded[path] = Fold(existing, change.Kind);
        }

        var result = new List<FolderChange>(folded.Count);
        foreach (string path in order)
        {
            if (!dropped.Contains(path) && folded.TryGetValue(path, out FolderChangeKind kind))
            {
                result.Add(new FolderChange(path, kind));
            }
        }

        return result;
    }

    /// <summary>
    /// Describes a coalesced set of changes as the payload a trigger's signal carries.
    /// </summary>
    /// <param name="root">The folder being watched, named once so the rest of the lines need not repeat it.</param>
    /// <param name="changes">The changes, already folded by <see cref="Coalesce"/>.</param>
    /// <param name="maxListed">How many files to name before counting the rest.</param>
    /// <returns>The payload text, or an empty string when there is nothing to report.</returns>
    public static string Describe(string? root, IReadOnlyList<FolderChange>? changes, int maxListed = DefaultMaxListed)
    {
        if (changes is null || changes.Count == 0)
        {
            return string.Empty;
        }

        int added = 0;
        int changed = 0;
        int removed = 0;
        int renamed = 0;

        foreach (FolderChange change in changes)
        {
            switch (change.Kind)
            {
                case FolderChangeKind.Added: added++; break;
                case FolderChangeKind.Changed: changed++; break;
                case FolderChangeKind.Removed: removed++; break;
                case FolderChangeKind.Renamed: renamed++; break;
            }
        }

        var counts = new List<string>(4);
        Add(counts, added, "added");
        Add(counts, changed, "changed");
        Add(counts, removed, "removed");
        Add(counts, renamed, "renamed");

        StringBuilder text = new();
        text.Append("Files changed in ")
            .Append(string.IsNullOrWhiteSpace(root) ? "the watched folder" : root)
            .Append(": ")
            .Append(string.Join(", ", counts))
            .Append('.');

        int limit = Math.Max(1, maxListed);
        for (int i = 0; i < changes.Count && i < limit; i++)
        {
            text.Append(Environment.NewLine)
                .Append("  ")
                .Append(Marker(changes[i].Kind))
                .Append(' ')
                .Append(changes[i].RelativePath);
        }

        int omitted = changes.Count - limit;
        if (omitted > 0)
        {
            text.Append(Environment.NewLine)
                .Append("  … and ")
                .Append(omitted.ToString(CultureInfo.InvariantCulture))
                .Append(" more.");
        }

        return text.ToString();
    }

    private static void Add(List<string> counts, int n, string label)
    {
        if (n > 0)
        {
            counts.Add($"{n.ToString(CultureInfo.InvariantCulture)} {label}");
        }
    }

    private static char Marker(FolderChangeKind kind) => kind switch
    {
        FolderChangeKind.Added => '+',
        FolderChangeKind.Removed => '-',
        FolderChangeKind.Renamed => '>',
        _ => '~',
    };

    // An addition absorbs the writes that finish it, and a rename absorbs them too — in both cases
    // the interesting fact is the first event, not the last. Everything else takes the newer kind.
    private static FolderChangeKind Fold(FolderChangeKind existing, FolderChangeKind incoming) =>
        (existing, incoming) switch
        {
            (FolderChangeKind.Added, FolderChangeKind.Changed) => FolderChangeKind.Added,
            (FolderChangeKind.Renamed, FolderChangeKind.Changed) => FolderChangeKind.Renamed,
            _ => incoming,
        };

    private static string Normalise(string path) => path.Replace('\\', '/').TrimStart('/');
}
