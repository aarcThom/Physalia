// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Physalia.GH.Assemblies;

internal static class AssemblyIO
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Serializes a selection of GH document objects to a <c>.assembly</c> JSON file.
    /// Positions are stored relative to the bounding-box top-left of the selection.
    /// Only wires between objects within the selection are captured; external connections are ignored.
    /// </summary>
    /// <param name="objects">The document objects to export.</param>
    /// <param name="filePath">Destination file path.</param>
    public static void Export(IEnumerable<IGH_DocumentObject> objects, string filePath)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        var components = new List<IGH_Component>();
        var standaloneParams = new List<IGH_Param>();
        var groups = new List<GH_Group>();
        var scribbles = new List<GH_Scribble>();

        foreach (var obj in objects)
        {
            switch (obj)
            {
                case IGH_Component c:
                    components.Add(c);
                    break;
                case IGH_Param p:
                    standaloneParams.Add(p);
                    break;
                case GH_Group g:
                    groups.Add(g);
                    break;
                case GH_Scribble s:
                    scribbles.Add(s);
                    break;
            }
        }

        var pivotBearing = components.Cast<IGH_DocumentObject>()
            .Concat(standaloneParams)
            .Concat(scribbles)
            .ToList();

        if (pivotBearing.Count == 0)
            return;

        float originX = pivotBearing.Min(o => o.Attributes.Pivot.X);
        float originY = pivotBearing.Min(o => o.Attributes.Pivot.Y);

        var idMap = new Dictionary<Guid, string>();
        for (int i = 0; i < components.Count; i++)
            idMap[components[i].InstanceGuid] = $"c{i}";
        for (int i = 0; i < standaloneParams.Count; i++)
            idMap[standaloneParams[i].InstanceGuid] = $"p{i}";
        for (int i = 0; i < groups.Count; i++)
            idMap[groups[i].InstanceGuid] = $"g{i}";
        for (int i = 0; i < scribbles.Count; i++)
            idMap[scribbles[i].InstanceGuid] = $"s{i}";

        // Maps output param InstanceGuid → (ownerLocalId, outputIndex).
        // Avoids fragile Attributes.GetTopLevel traversal for source resolution.
        var paramToId = new Dictionary<Guid, (string localId, int outputIndex)>();
        foreach (var comp in components)
        {
            for (int oi = 0; oi < comp.Params.Output.Count; oi++)
                paramToId[comp.Params.Output[oi].InstanceGuid] = (idMap[comp.InstanceGuid], oi);
        }
        foreach (var sp in standaloneParams)
            paramToId[sp.InstanceGuid] = (idMap[sp.InstanceGuid], 0);

        var objectDefs = new List<AssemblyObjectDef>();

        foreach (var comp in components)
        {
            objectDefs.Add(new AssemblyComponentDef(
                idMap[comp.InstanceGuid],
                comp.ComponentGuid,
                comp.NickName,
                comp.Attributes.Pivot.X - originX,
                comp.Attributes.Pivot.Y - originY));
        }

        foreach (var sp in standaloneParams)
        {
            objectDefs.Add(new AssemblyParamDef(
                idMap[sp.InstanceGuid],
                sp.ComponentGuid,
                sp.NickName,
                sp.Attributes.Pivot.X - originX,
                sp.Attributes.Pivot.Y - originY));
        }

        foreach (var group in groups)
        {
            var members = group.Objects()
                .Where(o => idMap.ContainsKey(o.InstanceGuid))
                .Select(o => idMap[o.InstanceGuid])
                .ToList();

            objectDefs.Add(new AssemblyGroupDef(
                idMap[group.InstanceGuid],
                group.NickName,
                ColorToHex(group.Colour),
                members));
        }

        foreach (var scribble in scribbles)
        {
            objectDefs.Add(new AssemblyScribbleDef(
                idMap[scribble.InstanceGuid],
                scribble.Text,
                scribble.Attributes.Pivot.X - originX,
                scribble.Attributes.Pivot.Y - originY));
        }

        var wires = new List<AssemblyWire>();

        foreach (var comp in components)
        {
            for (int pi = 0; pi < comp.Params.Input.Count; pi++)
            {
                foreach (var source in comp.Params.Input[pi].Sources)
                {
                    if (paramToId.TryGetValue(source.InstanceGuid, out var from))
                        wires.Add(new AssemblyWire(from.localId, from.outputIndex, idMap[comp.InstanceGuid], pi));
                }
            }
        }

        foreach (var sp in standaloneParams)
        {
            foreach (var source in sp.Sources)
            {
                if (paramToId.TryGetValue(source.InstanceGuid, out var from))
                    wires.Add(new AssemblyWire(from.localId, from.outputIndex, idMap[sp.InstanceGuid], 0));
            }
        }

        var definition = new AssemblyDefinition(
            Path.GetFileNameWithoutExtension(filePath),
            objectDefs,
            wires,
            new List<AssemblyExposedPort>(),
            new List<AssemblyExposedPort>());

        File.WriteAllText(filePath, JsonSerializer.Serialize(definition, _options));
    }

    /// <summary>
    /// Instantiates a <c>.assembly</c> JSON file onto a GH document at the given canvas origin.
    /// </summary>
    /// <param name="filePath">Path to the <c>.assembly</c> file.</param>
    /// <param name="document">The target GH document.</param>
    /// <param name="placementOrigin">Canvas position mapped to the assembly's bounding-box origin.</param>
    /// <returns>All newly created document objects, including groups.</returns>
    public static IReadOnlyList<IGH_DocumentObject> Import(
        string filePath,
        GH_Document document,
        PointF placementOrigin)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        string json = File.ReadAllText(filePath);
        var definition = JsonSerializer.Deserialize<AssemblyDefinition>(json, _options)
            ?? throw new InvalidOperationException("Failed to deserialize assembly definition.");

        var created = new Dictionary<string, IGH_DocumentObject>();
        var pendingGroups = new List<(AssemblyGroupDef Def, GH_Group Group)>();

        foreach (var objDef in definition.Objects)
        {
            switch (objDef)
            {
                case AssemblyComponentDef compDef:
                {
                    var obj = Instances.ComponentServer.EmitObject(compDef.TypeGuid)
                        ?? throw new InvalidOperationException($"Unknown component type GUID: {compDef.TypeGuid}");
                    obj.NickName = compDef.Nickname;
                    obj.CreateAttributes();
                    obj.Attributes.Pivot = new PointF(
                        placementOrigin.X + compDef.PivotX,
                        placementOrigin.Y + compDef.PivotY);
                    document.AddObject(obj, false);
                    created[compDef.Id] = obj;
                    break;
                }
                case AssemblyParamDef paramDef:
                {
                    var obj = Instances.ComponentServer.EmitObject(paramDef.TypeGuid)
                        ?? throw new InvalidOperationException($"Unknown param type GUID: {paramDef.TypeGuid}");
                    obj.NickName = paramDef.Nickname;
                    obj.CreateAttributes();
                    obj.Attributes.Pivot = new PointF(
                        placementOrigin.X + paramDef.PivotX,
                        placementOrigin.Y + paramDef.PivotY);
                    document.AddObject(obj, false);
                    created[paramDef.Id] = obj;
                    break;
                }
                case AssemblyScribbleDef scribbleDef:
                {
                    var scribble = new GH_Scribble();
                    scribble.Text = scribbleDef.Text;
                    scribble.CreateAttributes();
                    scribble.Attributes.Pivot = new PointF(
                        placementOrigin.X + scribbleDef.PivotX,
                        placementOrigin.Y + scribbleDef.PivotY);
                    document.AddObject(scribble, false);
                    created[scribbleDef.Id] = scribble;
                    break;
                }
                case AssemblyGroupDef groupDef:
                {
                    var group = new GH_Group();
                    group.NickName = groupDef.Label;
                    group.Colour = ColorFromHex(groupDef.Colour);
                    pendingGroups.Add((groupDef, group));
                    break;
                }
            }
        }

        // Add groups after all members exist so GH can compute bounds correctly.
        foreach (var (groupDef, group) in pendingGroups)
        {
            group.CreateAttributes();
            document.AddObject(group, false);
            foreach (var memberId in groupDef.Members)
            {
                if (created.TryGetValue(memberId, out var member))
                    group.AddObject(member.InstanceGuid);
            }
            created[groupDef.Id] = group;
        }

        foreach (var wire in definition.Wires)
        {
            if (!created.TryGetValue(wire.FromId, out var fromObj) ||
                !created.TryGetValue(wire.ToId, out var toObj))
                continue;

            var fromParam = GetOutputParam(fromObj, wire.FromOutputIndex);
            var toParam = GetInputParam(toObj, wire.ToInputIndex);

            if (fromParam != null && toParam != null)
                toParam.AddSource(fromParam);
        }

        return created.Values.ToList();
    }

    private static IGH_Param? GetOutputParam(IGH_DocumentObject obj, int index) => obj switch
    {
        IGH_Component comp when index < comp.Params.Output.Count => comp.Params.Output[index],
        IGH_Param param when index == 0 => param,
        _ => null,
    };

    private static IGH_Param? GetInputParam(IGH_DocumentObject obj, int index) => obj switch
    {
        IGH_Component comp when index < comp.Params.Input.Count => comp.Params.Input[index],
        IGH_Param param when index == 0 => param,
        _ => null,
    };

    private static string ColorToHex(Color c) =>
        $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8)
            return Color.FromArgb(
                Convert.ToInt32(hex[0..2], 16),
                Convert.ToInt32(hex[2..4], 16),
                Convert.ToInt32(hex[4..6], 16),
                Convert.ToInt32(hex[6..8], 16));
        return Color.FromArgb(
            Convert.ToInt32(hex[0..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16));
    }
}
