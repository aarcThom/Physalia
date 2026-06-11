// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;

namespace Physalia.GH.Goo;

/// <summary>
/// Base for Physalia goo wrappers around reference-type values. Owns validity and
/// duplication; subclasses supply type names, descriptions, and display strings.
/// </summary>
/// <typeparam name="TGoo">The concrete goo type (self-referencing, used by Duplicate).</typeparam>
/// <typeparam name="T">The wrapped value type.</typeparam>
public abstract class PhyGoo<TGoo, T> : GH_Goo<T>
    where TGoo : PhyGoo<TGoo, T>, new()
    where T : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PhyGoo{TGoo, T}"/> class with no value.
    /// </summary>
    protected PhyGoo()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PhyGoo{TGoo, T}"/> class wrapping the given value.
    /// </summary>
    /// <param name="value">The value to wrap.</param>
    protected PhyGoo(T value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    public override bool IsValid => Value is not null;

    /// <inheritdoc/>
    public override IGH_Goo Duplicate() => new TGoo { Value = Value };
}
