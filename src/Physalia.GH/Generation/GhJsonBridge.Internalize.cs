// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GhJSON.Core.SchemaModels;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Newtonsoft.Json;
using Rhino.Geometry;

namespace Physalia.GH.Generation;

/// <summary>
/// Post-placement repair of model-authored internalized data. The GhJSON library applies
/// <c>internalizedData</c> during Put, but its typed-prefix parser cannot cast into a GENERIC
/// parameter (e.g. Division's <c>A</c>/<c>B</c>, which are Generic Data) — the value lands as a
/// single null item that silently poisons everything downstream. This pass runs after every Put
/// that places model-authored components (full-document placement, patch adds, patch modifies):
/// wherever a param's placed persistent data does not match what the model authored, the
/// type-prefixed strings are parsed here and applied via <c>SetPersistentData</c>, whose own
/// casting handles typed and generic params alike.
/// </summary>
internal static partial class GhJsonBridge
{
    /// <summary>
    /// Re-applies authored internalized input values wherever the library's Put dropped or nulled
    /// them. Healthy params (item count matches, no nulls) are left untouched.
    /// </summary>
    /// <param name="components">The authored components (post-Fix, ids matching the placement).</param>
    /// <param name="resolveById">Resolves an authored component id to its placed live object.</param>
    /// <param name="warnings">Accumulates model-facing lines for values that could not be applied.</param>
    /// <returns>The number of params repaired (so callers can gate a re-solve).</returns>
    private static int RepairInternalizedData(
        IEnumerable<GhJsonComponent> components,
        Func<int, IGH_DocumentObject?> resolveById,
        List<string> warnings)
    {
        int repaired = 0;
        foreach (GhJsonComponent component in components)
        {
            if (component.Id is not int id || component.InputSettings is null)
            {
                continue;
            }

            IGH_DocumentObject? live = null;
            foreach (GhJsonParameterSettings settings in component.InputSettings)
            {
                if (settings.InternalizedData is null || string.IsNullOrWhiteSpace(settings.ParameterName))
                {
                    continue;
                }

                live ??= resolveById(id);
                IGH_Param? param = live is null ? null : FindInternalizeTarget(live, settings.ParameterName!);
                if (param is null)
                {
                    continue; // unresolved component/param was reported by the placement itself
                }

                List<string> authored = FlattenInternalizedValues(settings.InternalizedData);
                if (authored.Count == 0 || PersistentDataHealthy(param, authored.Count))
                {
                    continue;
                }

                var values = new List<object>(authored.Count);
                bool parsedAll = true;
                foreach (string entry in authored)
                {
                    if (TryParseInternalizedValue(entry, out object? value))
                    {
                        values.Add(value!);
                    }
                    else
                    {
                        warnings.Add($"internalized value '{entry}' on '{component.Name}' (id {id}) input '{settings.ParameterName}' has an unknown format and was not applied.");
                        parsedAll = false;
                        break;
                    }
                }

                if (!parsedAll)
                {
                    continue;
                }

                // Verify the repair actually took: a silent failure here cost a real session four
                // identical null rounds — better one loud warning than another guessing loop.
                if (TrySetPersistentData(param, values) && PersistentDataHealthy(param, values.Count))
                {
                    param.ExpireSolution(false);
                    repaired++;
                }
                else
                {
                    warnings.Add($"internalized value on '{component.Name}' (id {id}) input '{settings.ParameterName}' could not be applied cleanly — supply it by wire instead.");
                }
            }
        }

        return repaired;
    }

    // The param an inputSettings entry addresses: a component's input by full Name (then
    // NickName), or a floating param itself.
    private static IGH_Param? FindInternalizeTarget(IGH_DocumentObject live, string parameterName)
    {
        if (live is not IGH_Component component)
        {
            return live as IGH_Param;
        }

        return component.Params.Input.FirstOrDefault(p => p.Name == parameterName)
            ?? component.Params.Input.FirstOrDefault(p => p.NickName == parameterName);
    }

    // Flattens the authored internalizedData tree (path -> indexed items) into its value strings,
    // in key order. The library model's exact type is private detail — round-trip through JSON to
    // read it untyped, the same pattern RestoreFeedbackLinks uses for extension payloads.
    private static List<string> FlattenInternalizedValues(object internalizedData)
    {
        try
        {
            var tree = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
                JsonConvert.SerializeObject(internalizedData));
            return tree is null
                ? new List<string>()
                : tree.OrderBy(b => b.Key, StringComparer.Ordinal)
                    .SelectMany(b => b.Value.OrderBy(i => i.Key, StringComparer.Ordinal).Select(i => i.Value))
                    .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    // True when the placed param already holds exactly the authored number of items with no
    // nulls — the library applied the data correctly and the repair must not disturb it.
    private static bool PersistentDataHealthy(IGH_Param param, int authoredCount)
    {
        if (param.GetType().GetProperty("PersistentData")?.GetValue(param) is not IGH_Structure persistent)
        {
            return false;
        }

        return persistent.DataCount == authoredCount
            && persistent.AllData(false).All(goo => goo is not null);
    }

    // Parses one type-prefixed value string ("number:2", "pointXYZ:0,0,0", ...) into the raw .NET
    // value SetPersistentData can cast from. Invariant culture throughout.
    private static bool TryParseInternalizedValue(string entry, out object? value)
    {
        value = null;
        int colon = entry.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        string prefix = entry[..colon].Trim();
        string payload = entry[(colon + 1)..].Trim();

        static bool Num(string s, out double d) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

        static double[]? Triplet(string s)
        {
            string[] parts = s.Split(',');
            if (parts.Length != 3)
            {
                return null;
            }

            var result = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (!Num(parts[i], out result[i]))
                {
                    return null;
                }
            }

            return result;
        }

        switch (prefix)
        {
            case "number":
                if (Num(payload, out double number))
                {
                    value = number;
                }

                break;
            case "integer":
                if (int.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                {
                    value = integer;
                }

                break;
            case "boolean":
                if (bool.TryParse(payload, out bool flag))
                {
                    value = flag;
                }

                break;
            case "text":
                value = payload;
                break;
            case "pointXYZ":
                if (Triplet(payload) is { } pt)
                {
                    value = new Point3d(pt[0], pt[1], pt[2]);
                }

                break;
            case "vectorXYZ":
                if (Triplet(payload) is { } vec)
                {
                    value = new Vector3d(vec[0], vec[1], vec[2]);
                }

                break;
            case "planeOXY":
                string[] axes = payload.Split(';');
                if (axes.Length == 3
                    && Triplet(axes[0]) is { } o
                    && Triplet(axes[1]) is { } x
                    && Triplet(axes[2]) is { } y)
                {
                    value = new Plane(
                        new Point3d(o[0], o[1], o[2]),
                        new Vector3d(x[0], x[1], x[2]),
                        new Vector3d(y[0], y[1], y[2]));
                }

                break;
        }

        return value is not null;
    }

    // Applies raw values through GH_PersistentParam<T>.SetPersistentData(params object[]), whose
    // internal casting handles typed and generic params alike. Reached by reflection because the
    // method lives on the generic persistent base, not on IGH_Param. The existing records are
    // cleared first: SetPersistentData APPENDS to what the param already holds — without the
    // clear, the library's null survives at index 0 and the repaired value lands at index 1,
    // observed live as "B=2 (1 null)".
    private static bool TrySetPersistentData(IGH_Param param, List<object> values)
    {
        MethodInfo? setter = param.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (m.Name != "SetPersistentData")
                {
                    return false;
                }

                ParameterInfo[] args = m.GetParameters();
                return args.Length == 1 && args[0].ParameterType == typeof(object[]);
            });

        if (setter is null)
        {
            return false;
        }

        try
        {
            if (param.GetType().GetProperty("PersistentData")?.GetValue(param) is { } persistent)
            {
                persistent.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(persistent, null);
            }

            setter.Invoke(param, new object[] { values.ToArray() });
            return true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
    }
}
