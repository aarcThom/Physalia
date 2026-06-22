// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Diagnostics.CodeAnalysis;

namespace Physalia.Core.Common;

/// <summary>
/// A two-case discriminated union representing either a successful value or an error.
/// Pattern-match on <see cref="Ok"/> and <see cref="Err"/> to consume, or use the
/// <see cref="IsOk(out T, out E)"/> / <see cref="IsErr(out E, out T)"/> / <see cref="Match{TResult}"/>
/// helpers to avoid the verbose cast-from-base pattern.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
/// <typeparam name="E">The error type.</typeparam>
public abstract record Result<T, E>
{
    /// <summary>
    /// The success case. Contains the result value.
    /// </summary>
    /// <param name="Value">The success value.</param>
    public sealed record Ok(T Value) : Result<T, E>;

    /// <summary>
    /// The failure case. Contains the error.
    /// </summary>
    /// <param name="Error">The error value.</param>
    public sealed record Err(E Error) : Result<T, E>;

    /// <summary>
    /// Tests for the success case, handing back the value when it matches and the error otherwise.
    /// Replaces the <c>is Ok</c> check followed by a cast to read <c>.Value</c>.
    /// </summary>
    /// <param name="value">The success value when this is <see cref="Ok"/>.</param>
    /// <param name="error">The error when this is <see cref="Err"/>.</param>
    /// <returns>true when this is <see cref="Ok"/>; otherwise false.</returns>
    public bool IsOk([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out E error)
    {
        if (this is Ok ok)
        {
            value = ok.Value;
            error = default;
            return true;
        }

        value = default;
        error = ((Err)this).Error;
        return false;
    }

    /// <summary>
    /// Tests for the failure case, handing back the error when it matches and the value otherwise.
    /// Lets a caller short-circuit on failure (<c>if (result.IsErr(out var err, out var value)) …</c>)
    /// and then use the value, which the compiler knows is non-null on the false branch.
    /// </summary>
    /// <param name="error">The error when this is <see cref="Err"/>.</param>
    /// <param name="value">The success value when this is <see cref="Ok"/>.</param>
    /// <returns>true when this is <see cref="Err"/>; otherwise false.</returns>
    public bool IsErr([MaybeNullWhen(false)] out E error, [MaybeNullWhen(true)] out T value)
    {
        bool ok = IsOk(out value, out error);
        return !ok;
    }

    /// <summary>
    /// Collapses both cases to a single value by applying the matching function.
    /// </summary>
    /// <typeparam name="TResult">The common result type.</typeparam>
    /// <param name="ok">Applied to the value when this is <see cref="Ok"/>.</param>
    /// <param name="err">Applied to the error when this is <see cref="Err"/>.</param>
    /// <returns>The result of the function for the present case.</returns>
    public TResult Match<TResult>(Func<T, TResult> ok, Func<E, TResult> err) =>
        this is Ok o ? ok(o.Value) : err(((Err)this).Error);

    /// <summary>
    /// Runs the action matching the present case.
    /// </summary>
    /// <param name="ok">Run with the value when this is <see cref="Ok"/>.</param>
    /// <param name="err">Run with the error when this is <see cref="Err"/>.</param>
    public void Switch(Action<T> ok, Action<E> err)
    {
        if (this is Ok o)
        {
            ok(o.Value);
        }
        else
        {
            err(((Err)this).Error);
        }
    }
}
