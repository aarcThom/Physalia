// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;

namespace Physalia.GH.Components;

/// <summary>
/// A named set of selectable string values for a single input slot.
/// </summary>
/// <param name="Name">The input parameter name this set belongs to.</param>
/// <param name="Values">The available string values for that input.</param>
public record PickableInput(string Name, IReadOnlyList<string> Values);

/// <summary>
/// Read-only contract for upstream consumers (e.g. a Picker component).
/// Exposes one or more named inputs, each with its own list of available values.
/// </summary>
public interface IPickableValues
{
    IReadOnlyList<PickableInput> Inputs { get; }
}

/// <summary>
/// Full contract for the component that owns and manages the values.
/// </summary>
public interface IPickableValuesSource : IPickableValues
{
    void SetValues(string inputName, IEnumerable<string> values);
    void ResetValues();
}
