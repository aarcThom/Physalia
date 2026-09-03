// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;

namespace Physalia.GH.Components;

/// <summary>
/// A named set of selectable string values for a single input slot.
/// </summary>
/// <param name="Name">The input parameter name this set belongs to.</param>
/// <param name="Values">The available string values for that input.</param>
/// <param name="IsSettled">
/// Whether <paramref name="Values"/> is the authoritative list. False marks a PROVISIONAL list —
/// a seed shown while the real one is being fetched — which a Picker must never treat as the whole
/// truth: a Picker solves BEFORE the component it feeds, so on the first solve after a file opens
/// the seed is all there is, and snapping a restored pick onto it silently swaps the choice for
/// the seed's first entry. Defaults to true, which is correct for any list built from a fixed set
/// or read synchronously.
/// </param>
public record PickableInput(string Name, IReadOnlyList<string> Values, bool IsSettled = true);

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
