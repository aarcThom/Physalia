// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Physalia.Core.Config;
using Physalia.Core.Grounding.Clusters;

namespace Physalia.GH.Generation;

/// <summary>
/// Reads the <c>CLUSTERS</c> folders and builds a <see cref="ClusterCatalog"/>: one
/// entry per cluster file, each carrying the optional human description from <c>clusters.json</c>
/// and the input/output parameter signature introspected from the cluster file itself. This is the
/// single source of truth for cluster grounding (the producer), the chat-window selection UI, the
/// <c>/c/</c> prompt autocomplete, and placement (resolving a referenced name to its file).
/// <para>The catalog is cached and rebuilt only when the folder's contents change. Building loads
/// Grasshopper objects, so call it from the main (solve) thread.</para>
/// </summary>
public static class ClusterCatalogProvider
{
    // .ghcluster is the dedicated cluster format; .gh/.ghx also load via CreateFromFilePath.
    private static readonly string[] ClusterExtensions = { ".ghcluster", ".gh", ".ghx" };
    private const string ManifestFileName = "clusters.json";

    private static readonly object Gate = new object();
    private static ClusterCatalog? _cache;
    private static string? _cacheSignature;

    /// <summary>
    /// Gets the <c>CLUSTERS</c> folders, in precedence order: the user's own, then the one shipped
    /// with the plug-in.
    /// </summary>
    /// <remarks>
    /// The user's data folder OVERLAYS the shipped clusters — a cluster file of the same name
    /// shadows a shipped one, and a <c>clusters.json</c> entry of the same file name shadows its
    /// description — so the shipped set keeps arriving with updates while the user's own survive
    /// them. Before 2026-09-09 there was one folder, inside the install directory, which a silent
    /// package update replaced wholesale.
    /// </remarks>
    public static IReadOnlyList<string> ClusterFolders =>
        PhyData.SearchPath(Assembly.GetExecutingAssembly(), PhyData.Clusters);

    /// <summary>
    /// Builds (or returns a cached) catalog of the clusters in <see cref="ClusterFolders"/>. The
    /// cache is invalidated automatically when a cluster file or the manifest is added, removed, or
    /// edited; pass <paramref name="forceRefresh"/> to rebuild unconditionally.
    /// </summary>
    /// <param name="forceRefresh">True to ignore the cache and rebuild.</param>
    /// <returns>The cluster catalog (empty when no folder exists or none holds a cluster file).</returns>
    public static ClusterCatalog GetCatalog(bool forceRefresh = false)
    {
        IReadOnlyList<string> folders = ClusterFolders;
        string signature = string.Join("|", folders.Select(ComputeSignature));

        lock (Gate)
        {
            if (!forceRefresh && _cache is not null && _cacheSignature == signature)
            {
                return _cache;
            }

            ClusterCatalog catalog = Build(folders);
            _cache = catalog;
            _cacheSignature = signature;
            return catalog;
        }
    }

    // A stable fingerprint of the folder: each cluster file and the manifest by path + last-write
    // ticks. Any add/remove/edit changes the string, so the cache rebuilds exactly when it must.
    private static string ComputeSignature(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (string path in EnumerateClusterFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(path).Append('|').Append(SafeWriteTicks(path)).Append(';');
        }

        string manifest = Path.Combine(folder, ManifestFileName);
        if (File.Exists(manifest))
        {
            sb.Append(manifest).Append('|').Append(SafeWriteTicks(manifest));
        }

        return sb.ToString();
    }

    private static long SafeWriteTicks(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch
        {
            return 0L;
        }
    }

    private static ClusterCatalog Build(IReadOnlyList<string> folders)
    {
        // Manifests are read in REVERSE precedence order so a higher-precedence entry overwrites a
        // lower one: one merged map, the user's description winning for a file name they both carry.
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders.Reverse())
        {
            foreach (KeyValuePair<string, string> pair in ReadManifest(folder))
            {
                descriptions[pair.Key] = pair.Value;
            }
        }

        var entries = new List<ClusterEntry>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in folders)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            foreach (string path in EnumerateClusterFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                // The cluster's identity is its file name (without extension): predictable, user-renameable,
                // and exactly what the user types after "/c/". The cluster's internal display name is ignored.
                string name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // A name already taken by a higher-precedence folder is shadowed, not duplicated:
                // the user's copy of a shipped cluster is the one they meant.
                if (!claimed.Add(name))
                {
                    continue;
                }

                string fileName = Path.GetFileName(path);
                descriptions.TryGetValue(fileName, out string? description);

                (IReadOnlyList<ClusterPort> inputs, IReadOnlyList<ClusterPort> outputs) = Introspect(path);
                entries.Add(new ClusterEntry(name, path, description ?? string.Empty, inputs, outputs));
            }
        }

        // Sorted by name rather than left folder-by-folder, so which root a cluster came from is
        // invisible to every consumer — the selection UI, the "/c/" autocomplete and the grounding.
        return new ClusterCatalog(entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IEnumerable<string> EnumerateClusterFiles(string folder) => Directory
        .EnumerateFiles(folder)
        .Where(p => ClusterExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));

    // Reads clusters.json into a file-name -> description map (case-insensitive on file name).
    // A missing/malformed manifest yields an empty map; descriptions are optional grounding sugar.
    private static Dictionary<string, string> ReadManifest(string folder)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(folder))
        {
            // Guarded because an empty root would compose a RELATIVE "clusters.json", which resolves
            // against Rhino's working directory — a folder that has nothing to do with us.
            return map;
        }

        string manifest = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(manifest))
        {
            return map;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return map;
            }

            foreach (JsonElement element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("file", out JsonElement fileProp)
                    || fileProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? file = fileProp.GetString();
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                string description = element.TryGetProperty("description", out JsonElement descProp)
                    && descProp.ValueKind == JsonValueKind.String
                        ? descProp.GetString() ?? string.Empty
                        : string.Empty;

                map[file!] = description;
            }
        }
        catch
        {
            // A broken manifest must not break grounding — clusters still appear without descriptions.
        }

        return map;
    }

    // Loads the cluster from its file and reads its input/output parameter interface. Returns empty
    // lists when the file cannot be loaded, so a single bad cluster never breaks the whole catalog.
    private static (IReadOnlyList<ClusterPort> Inputs, IReadOnlyList<ClusterPort> Outputs) Introspect(string path)
    {
        try
        {
            var cluster = new GH_Cluster();
            cluster.CreateFromFilePath(path);
            return (ReadPorts(cluster.Params.Input), ReadPorts(cluster.Params.Output));
        }
        catch
        {
            return (Array.Empty<ClusterPort>(), Array.Empty<ClusterPort>());
        }
    }

    private static IReadOnlyList<ClusterPort> ReadPorts(IEnumerable<IGH_Param> @params)
    {
        var ports = new List<ClusterPort>();
        foreach (IGH_Param param in @params)
        {
            // A cluster labels its ports with the nickname the author set on each input/output hook
            // ("top curve", "bottom curve") — that is what the user sees on the cluster and the only
            // thing that distinguishes same-typed ports, so prefer it. The Name is usually the generic
            // type ("Curve"); fall back to it only when the nickname is blank.
            string portName = !string.IsNullOrWhiteSpace(param.NickName) ? param.NickName : param.Name ?? string.Empty;
            ports.Add(new ClusterPort(portName, ComponentSignatureProvider.SafeTypeName(param)));
        }

        return ports;
    }
}
