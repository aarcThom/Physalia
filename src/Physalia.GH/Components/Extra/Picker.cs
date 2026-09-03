// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.GH.Attributes;

namespace Physalia.GH.Components;

/// <summary>
/// Outputs a single string value chosen from a connected <see cref="IPickableValues"/> component.
/// Right-click the component to select a value; defaults to the first available value.
/// </summary>
public class Picker : PhyBase
{
    private string _selectedValue = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="Picker"/> class.
    /// </summary>
    public Picker()
        : base("Picker", "Pick", "Offers whatever choices the component it feeds knows about, and passes back the one you pick. Several Physalia components place one of these beside themselves automatically.", "Extra")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("2D14368F-0302-4D08-BDEC-61DD6A28732C");

    /// <summary>Gets the currently selected value.</summary>
    internal string SelectedValue => _selectedValue;

    /// <summary>Gets available values from the connected pickable component, or an empty list.</summary>
    internal IReadOnlyList<string> AvailableValues
    {
        get
        {
            var pickable = FindPickableInput();
            return pickable?.Values ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Gets the values to offer in the selection menus: everything on offer, with the current
    /// selection prepended when the source is not offering it. A remembered pick the source cannot
    /// currently list — because its list is still provisional, or the endpoint could not be
    /// reached — must stay visible and checked, or the menu says the choice was never made.
    /// </summary>
    internal IReadOnlyList<string> MenuValues
    {
        get
        {
            var values = AvailableValues;
            if (string.IsNullOrEmpty(_selectedValue) || values.Contains(_selectedValue))
                return values;

            var withCurrent = new List<string>(values.Count + 1) { _selectedValue };
            withCurrent.AddRange(values);
            return withCurrent;
        }
    }

    /// <summary>Sets the selected value (called from <see cref="PickerAttrib"/> on menu selection).</summary>
    /// <param name="value">The newly selected value.</param>
    internal void SetSelectedValue(string value) => _selectedValue = value;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new PickerAttrib(this);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Value", "V", "The choice you made.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var pickable = FindPickableInput();

        if (pickable == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Connect the output to an input whose name matches one of the source component's inputs.");
            DA.SetData(0, string.Empty);
            return;
        }

        var values = pickable.Values;

        // Falling back to the first offer is only safe once the source's list is AUTHORITATIVE.
        // A Picker solves before the component it feeds, so on the first solve after a file opens
        // a provisional list (the seed a component shows while its real list is being fetched) is
        // all there is; snapping onto it would swap the restored pick for the seed's first entry —
        // and, since the snap writes back to _selectedValue, lose the saved choice for good. An
        // empty list is treated the same way: nothing on offer is not evidence the pick is wrong.
        if (!string.IsNullOrEmpty(_selectedValue) && !values.Contains(_selectedValue))
        {
            if (pickable.IsSettled && values.Count > 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Remark,
                    $"'{_selectedValue}' is no longer offered here — using '{values[0]}'.");
                _selectedValue = values[0];
            }
        }
        else if (string.IsNullOrEmpty(_selectedValue) && pickable.IsSettled && values.Count > 0)
        {
            _selectedValue = values[0];
        }

        DA.SetData(0, _selectedValue);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);

        var pickable = FindPickableInput();
        if (pickable == null) return;

        Menu_AppendSeparator(menu);

        foreach (var value in MenuValues)
        {
            var item = Menu_AppendItem(menu, value, OnValueSelected, true, value == _selectedValue);
            item.Tag = value;
        }
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("SelectedValue", _selectedValue);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        if (reader.ItemExists("SelectedValue"))
            _selectedValue = reader.GetString("SelectedValue");
        return base.Read(reader);
    }

    private PickableInput? FindPickableInput()
    {
        foreach (var recipient in Params.Output[0].Recipients)
        {
            var topLevel = recipient.Attributes?.GetTopLevel?.DocObject;
            if (topLevel is IPickableValues pickable)
            {
                foreach (var input in pickable.Inputs)
                {
                    if (input.Name == recipient.Name)
                        return input;
                }
            }
        }

        return null;
    }

    private void OnValueSelected(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item && item.Tag is string value)
        {
            _selectedValue = value;
            ExpireSolution(true);
        }
    }
}
