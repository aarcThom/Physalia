using System.Collections.Generic;

namespace Physalia.GH.Components;

/// <summary>
/// Read-only contract for upstream consumers (e.g. a value picker component).
/// </summary>
public interface IPickableValues
{
    string InputName { get; }
    IReadOnlyList<string> Values { get; }
}

/// <summary>
/// Full contract for the component that owns and manages the values.
/// </summary>
public interface IPickableValuesSource : IPickableValues
{
    void SetValues(IEnumerable<string> values);
    void ResetValues();
}
