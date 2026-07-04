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
        : base("Picker", "Pick", "Picks a value from a connected component's available values.", "Extra")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.secondary;

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
        pManager.AddTextParameter("Value", "V", "The selected value.", GH_ParamAccess.item);
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

        if (values.Count == 0)
        {
            DA.SetData(0, string.Empty);
            return;
        }

        if (string.IsNullOrEmpty(_selectedValue) || !values.Contains(_selectedValue))
            _selectedValue = values[0];

        DA.SetData(0, _selectedValue);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);

        var pickable = FindPickableInput();
        if (pickable == null) return;

        Menu_AppendSeparator(menu);

        foreach (var value in pickable.Values)
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
