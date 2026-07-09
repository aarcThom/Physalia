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
using Physalia.Core.Grounding.Clusters;

namespace Physalia.GH.Generation;

/// <summary>
/// Reads the user's <c>Files/CLUSTERS</c> folder and builds a <see cref="ClusterCatalog"/>: one
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
    /// Gets the absolute path to the <c>Files/CLUSTERS</c> folder beside the assembly, or an empty
    /// string when the assembly location is unknown.
    /// </summary>
    public static string ClustersFolder
    {
        get
        {
            string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return assemblyDir is null ? string.Empty : Path.Combine(assemblyDir, "Files", "CLUSTERS");
        }
    }

    /// <summary>
    /// Builds (or returns a cached) catalog of the clusters in <see cref="ClustersFolder"/>. The
    /// cache is invalidated automatically when a cluster file or the manifest is added, removed, or
    /// edited; pass <paramref name="forceRefresh"/> to rebuild unconditionally.
    /// </summary>
    /// <param name="forceRefresh">True to ignore the cache and rebuild.</param>
    /// <returns>The cluster catalog (empty when the folder is missing or has no cluster files).</returns>
    public static ClusterCatalog GetCatalog(bool forceRefresh = false)
    {
        string folder = ClustersFolder;
        string signature = ComputeSignature(folder);

        lock (Gate)
        {
            if (!forceRefresh && _cache is not null && _cacheSignature == signature)
            {
                return _cache;
            }

            ClusterCatalog catalog = Build(folder);
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

    private static ClusterCatalog Build(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return new ClusterCatalog(Array.Empty<ClusterEntry>());
        }

        Dictionary<string, string> descriptions = ReadManifest(folder);
        var entries = new List<ClusterEntry>();

        foreach (string path in EnumerateClusterFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            // The cluster's identity is its file name (without extension): predictable, user-renameable,
            // and exactly what the user types after "/c/". The cluster's internal display name is ignored.
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string fileName = Path.GetFileName(path);
            descriptions.TryGetValue(fileName, out string? description);

            (IReadOnlyList<ClusterPort> inputs, IReadOnlyList<ClusterPort> outputs) = Introspect(path);
            entries.Add(new ClusterEntry(name, path, description ?? string.Empty, inputs, outputs));
        }

        return new ClusterCatalog(entries);
    }

    private static IEnumerable<string> EnumerateClusterFiles(string folder) => Directory
        .EnumerateFiles(folder)
        .Where(p => ClusterExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));

    // Reads clusters.json into a file-name -> description map (case-insensitive on file name).
    // A missing/malformed manifest yields an empty map; descriptions are optional grounding sugar.
    private static Dictionary<string, string> ReadManifest(string folder)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
