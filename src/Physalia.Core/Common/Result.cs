// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// A two-case discriminated union representing either a successful value or an error.
/// Pattern-match on <see cref="Ok"/> and <see cref="Err"/> to consume.
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
}
