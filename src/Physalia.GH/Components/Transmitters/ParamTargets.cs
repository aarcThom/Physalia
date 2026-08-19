// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GHPanel = Grasshopper.Kernel.Special.GH_Panel;

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
    /// Whether an object on the host canvas can receive a transmitted value at all — everything
    /// <see cref="CanHold"/> covers, plus a Panel.
    ///
    /// <para>A Panel is the exception both wire-like transmitters have to make: it is a
    /// <c>GH_Param</c> but NOT a <c>GH_PersistentParam</c>, so it has no persistent data to write and
    /// takes its content through <c>SetUserText</c> instead. It is also the target a user reaches for
    /// first when they want to SEE what is being transmitted, so refusing it would be the wrong kind
    /// of strict — a panel shows text, and everything has a text form.</para>
    /// </summary>
    /// <param name="candidate">An object on the user's canvas.</param>
    /// <returns>true when a value can be delivered into it.</returns>
    internal static bool CanHoldOrDisplay(IGH_DocumentObject candidate) =>
        candidate is GHPanel || CanHold(candidate);

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
    /// <para>An item the param refuses outright is offered again as TEXT, because a great many
    /// inputs are text inputs and a wire into one of them would have delivered exactly that — the
    /// value's own string form, the thing a Panel shows. Nothing has to opt in: a param that cannot
    /// read the geometry AND cannot read a string refuses both and is counted, so the fallback only
    /// ever fires where it is the right answer.</para>
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

                // The value itself, and failing that the text a wire into a text input would have
                // carried. Both go through the param's own cast, so neither is a special case here.
                object? value = ToItemType(item) ?? ToItemType(new GH_String(ItemText(item)));

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

        object? ToItemType(IGH_Goo candidate) => itemType.IsInstanceOfType(candidate)
            ? candidate
            : cast?.Invoke(param, new object?[] { candidate });
    }

    /// <summary>
    /// Writes a whole tree into a Panel AS A LIST — one item per line, which is the only shape a
    /// Panel's own storage can carry.
    ///
    /// <para>A Panel holds a single string and rebuilds its data from it: with <c>Multiline</c> off
    /// it splits that string into ONE ITEM PER LINE, so a list of geometry lands as a list of the
    /// same length, each piece cast to text on its own — the structure a wire would have delivered,
    /// not one blob. <c>Multiline</c> is therefore forced off whenever there is more than one item,
    /// since leaving it on would collapse them all back into a single value. A newline inside an
    /// item's own text is flattened to a space, because the line count IS the item count here.</para>
    ///
    /// <para><b>Branching cannot survive.</b> A Panel parses no paths back out of its text — writing
    /// Grasshopper's own <c>{0;0}</c> headers would land them as data items, which is worse than
    /// losing the branching — so a tree arrives flattened and the caller says so. A Text parameter
    /// takes the same values with the tree intact, and is where a tree belongs.</para>
    /// </summary>
    /// <typeparam name="T">The goo type the tree is read as.</typeparam>
    /// <param name="panel">The Panel to write into.</param>
    /// <param name="tree">The tree to write.</param>
    internal static void WritePanel<T>(GHPanel panel, GH_Structure<T> tree)
        where T : IGH_Goo
    {
        List<string> lines = new();

        foreach (IGH_Goo item in tree.AllData(true))
        {
            lines.Add(OneLine(ItemText(item)));
        }

        if (lines.Count > 1)
        {
            panel.Properties.Multiline = false;
        }

        panel.SetUserText(string.Join(Environment.NewLine, lines));
    }

    // One item, one line: any newline inside a value's own text would otherwise be read back as an
    // extra item, since a Panel splits its content by line.
    private static string OneLine(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>
    /// A single value's text form: Grasshopper's own conversion where it has one, and the goo's
    /// <c>ToString</c> otherwise — which for geometry is the type name Panels already show ("Brep",
    /// "Mesh"). Never null, because a value that cannot be described is worse than one described
    /// plainly.
    /// </summary>
    /// <param name="item">The value to describe.</param>
    /// <returns>The value's text form.</returns>
    internal static string ItemText(IGH_Goo item) =>
        GH_Convert.ToString(item, out string text, GH_Conversion.Both)
            ? text
            : item.ToString() ?? string.Empty;

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
