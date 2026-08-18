// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Physalia.GH.Components;

/// <summary>
/// How a transmitter that behaves like an ordinary wire finds its target and writes a value into it.
///
/// <para>Two transmitters connect this way — <see cref="TextTransmitter"/> and
/// <see cref="GeometryTransmitter"/> — and neither pushes CODE into a component the way the script
/// transmitters do: they internalise a value in a parameter, which is what a wire delivering the
/// same data would leave behind. That is one mechanism, so it lives in one place: which objects can
/// receive at all, which input a drop on a component means, and the write itself.</para>
///
/// <para>The write goes through <c>GH_PersistentParam&lt;T&gt;</c>, whose members are only reachable
/// at runtime — <c>T</c> is the target param's business and is not known until the link is made —
/// hence the reflection throughout. Every lookup is by the exact signature Grasshopper exposes, and
/// every one of them falls back rather than throwing, so a param that does not offer the member
/// simply refuses the value instead of taking down a solve.</para>
/// </summary>
internal static class ParamTargets
{
    // How close a drop must land to an input's grip to claim it, before falling back to the row under
    // the cursor. Matches the reach Grasshopper gives its own wire ends.
    private const float GripSnap = 12f;

    /// <summary>
    /// Gets the setter a parameter holds its own value through, or null when it has none.
    ///
    /// <para><c>GH_PersistentParam&lt;T&gt;.SetPersistentData(params object[])</c> casts each object
    /// into <c>T</c> the same way an incoming wire's data is cast. There is no non-generic interface
    /// exposing it and <c>T</c> is only known at runtime, so it is found by name. Its presence is
    /// also the test for "can this parameter hold a value at all".</para>
    /// </summary>
    /// <param name="param">A parameter on the user's canvas.</param>
    /// <returns>The persistent-data setter, or null.</returns>
    internal static MethodInfo? PersistentSetter(IGH_Param param) =>
        param.GetType().GetMethod("SetPersistentData", new[] { typeof(object[]) });

    /// <summary>
    /// Whether an object on the host canvas can hold a transmitted value: any parameter with its own
    /// persistent data — which is what a component's input IS, so this covers inputs of every type.
    /// The value is cast into whatever the input holds, exactly as a wire's would be.
    /// </summary>
    /// <param name="candidate">An object on the user's canvas.</param>
    /// <returns>true when a value can be delivered into it.</returns>
    internal static bool CanHold(IGH_DocumentObject candidate) =>
        candidate is IGH_Param param && PersistentSetter(param) is not null;

    /// <summary>
    /// The object a drop on a node should actually link. A node that holds its own value (a floating
    /// param, a Panel) takes it directly; a component is entered through one of its inputs, chosen
    /// the way Grasshopper chooses one for a wire — the nearest input GRIP, then the input row under
    /// the cursor, then the first input, so aiming at the icon still does the obvious thing.
    /// </summary>
    /// <param name="hit">The node the drop landed on.</param>
    /// <param name="dropPoint">The drop point, in canvas coordinates.</param>
    /// <param name="canReceive">
    /// The transmitter's own test for a direct hit — wider than <see cref="CanHold"/> where the
    /// transmitter can write into something that is not a persistent param (a Panel takes text).
    /// </param>
    /// <returns>The object to link, or null when the drop meant nothing.</returns>
    internal static IGH_DocumentObject? RefineDropTarget(
        IGH_DocumentObject hit,
        PointF dropPoint,
        Func<IGH_DocumentObject, bool> canReceive)
    {
        if (canReceive(hit))
        {
            return hit;
        }

        if (hit is not IGH_Component component)
        {
            return null;
        }

        IGH_Param? first = null;
        IGH_Param? underCursor = null;
        IGH_Param? nearestGrip = null;
        float nearest = GripSnap;

        foreach (IGH_Param input in component.Params.Input)
        {
            if (PersistentSetter(input) is null)
            {
                continue;
            }

            first ??= input;

            if (input.Attributes.Bounds.Contains(dropPoint))
            {
                underCursor ??= input;
            }

            if (input.Attributes is { HasInputGrip: true } attributes)
            {
                PointF grip = attributes.InputGrip;
                float distance = (float)Math.Sqrt(
                    ((grip.X - dropPoint.X) * (grip.X - dropPoint.X))
                    + ((grip.Y - dropPoint.Y) * (grip.Y - dropPoint.Y)));

                if (distance < nearest)
                {
                    nearest = distance;
                    nearestGrip = input;
                }
            }
        }

        return nearestGrip ?? underCursor ?? first;
    }

    /// <summary>
    /// How many values a parameter ended up holding, read back through the same runtime-typed surface
    /// the setter came from. Unknown (null) counts as delivered — never invent a failure.
    /// </summary>
    /// <param name="param">The parameter that was written to.</param>
    /// <returns>The persistent data count, or null when the parameter does not report one.</returns>
    internal static int? DeliveredCount(IGH_Param param) =>
        param.GetType().GetProperty("PersistentDataCount")?.GetValue(param) as int?;

    /// <summary>
    /// Internalises a whole data tree in a parameter, BRANCHING INTACT — the same values on the same
    /// paths a wire carrying that tree would have delivered.
    ///
    /// <para>The public <c>SetPersistentData(params object[])</c> can only make one flat branch, so
    /// the tree is rebuilt as the param's own <c>GH_Structure&lt;T&gt;</c> and handed to the
    /// structure-taking overload. Each item is cast with the param's own <c>Cast_Object</c> — the
    /// very conversion the flat setter performs, so a Brep entering a Mesh input converts here
    /// exactly as it would through a wire — and an item the param cannot read is counted rather than
    /// silently dropped, since "some of it arrived" is the one outcome a user cannot see.</para>
    ///
    /// <para>If a Grasshopper build ever stops offering those members the write still happens, flat:
    /// delivering the values without the branching beats delivering nothing.</para>
    /// </summary>
    /// <typeparam name="T">The goo type the incoming tree is read as.</typeparam>
    /// <param name="param">The target parameter on the user's canvas.</param>
    /// <param name="tree">The tree to internalise.</param>
    /// <param name="rejected">How many items the parameter could not read.</param>
    /// <returns>null on success, or what went wrong.</returns>
    internal static string? WriteTree<T>(IGH_Param param, GH_Structure<T> tree, out int rejected)
        where T : IGH_Goo
    {
        rejected = 0;

        if (PersistentBase(param.GetType()) is not { } persistentBase)
        {
            return $"\"{param.NickName}\" cannot hold a value of its own.";
        }

        Type itemType = persistentBase.GetGenericArguments()[0];
        Type structureType = typeof(GH_Structure<>).MakeGenericType(itemType);

        MethodInfo? append = structureType.GetMethod("Append", new[] { itemType, typeof(GH_Path) });
        MethodInfo? setTree = persistentBase.GetMethod("SetPersistentData", new[] { structureType });
        MethodInfo? cast =
            persistentBase.GetMethod("Cast_Object", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? persistentBase.GetMethod("PreferredCast", BindingFlags.Instance | BindingFlags.NonPublic);

        if (append is null || setTree is null)
        {
            return WriteFlat(param, tree, out rejected);
        }

        object structure = Activator.CreateInstance(structureType)!;

        for (int branch = 0; branch < tree.PathCount; branch++)
        {
            GH_Path path = tree.Paths[branch];

            foreach (T item in tree.Branches[branch])
            {
                if (item is null)
                {
                    continue;
                }

                object? value = itemType.IsInstanceOfType(item)
                    ? item
                    : cast?.Invoke(param, new object?[] { item });

                if (value is null)
                {
                    rejected++;
                    continue;
                }

                append.Invoke(structure, new[] { value, path });
            }
        }

        setTree.Invoke(param, new[] { structure });
        return null;
    }

    // The constructed GH_PersistentParam<T> in a param's base chain, which is where every member used
    // above is declared. Null for a param that has no persistent data of its own.
    private static Type? PersistentBase(Type paramType)
    {
        for (Type? type = paramType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(GH_PersistentParam<>))
            {
                return type;
            }
        }

        return null;
    }

    // The last-resort write: every value, one branch, through the public setter. Reached only if a
    // Grasshopper build stops offering the structure overload — the branching is lost, the data is not.
    private static string? WriteFlat<T>(IGH_Param param, GH_Structure<T> tree, out int rejected)
        where T : IGH_Goo
    {
        rejected = 0;

        if (PersistentSetter(param) is not { } setter)
        {
            return $"\"{param.NickName}\" cannot hold a value of its own.";
        }

        List<object> items = new();
        foreach (IGH_Goo item in tree.AllData(true))
        {
            items.Add(item);
        }

        // params object[] through reflection: one argument, itself an object[].
        setter.Invoke(param, new object[] { items.ToArray() });
        return null;
    }
}
