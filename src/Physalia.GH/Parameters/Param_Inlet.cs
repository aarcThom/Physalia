// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace Physalia.GH.Parameters;

/// <summary>
/// One input on a harness proxy, standing for a Receiver inside the harness.
///
/// <para>Generic and tree-access on purpose. Geometry is what these mostly carry, and it rides
/// through a generic parameter untouched — while the scalars a goal condition is stated in (a target
/// height, a count, a name) ride through too, which a geometry parameter would refuse. The branching
/// is data: what the canvas computed arrives inside the harness on the same paths it left on.</para>
///
/// <para>It exists as its own type for one reason: <see cref="ReceiverId"/>. The proxy's inputs are
/// derived from the harness's contents and are rebuilt as Receivers come and go, so each one has to
/// remember WHICH Receiver it belongs to — through a save and reload, and across a reorder. Binding
/// by position instead would hand one Receiver's data to another as soon as the nodes inside were
/// moved; rebuilding a parameter that already exists would drop the wire feeding it.</para>
/// </summary>
public class Param_Inlet : Param_GenericObject
{
    // Archive key for the bound Receiver. Deliberately prefixed: this parameter's archive is the stock
    // generic-parameter one, and a collision with a Grasshopper key would be silent.
    private const string ReceiverKey = "PhyInletReceiver";

    /// <summary>
    /// Initializes a new instance of the <see cref="Param_Inlet"/> class.
    /// </summary>
    public Param_Inlet()
    {
        Access = GH_ParamAccess.tree;
        Optional = true;
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("1B6F3A57-9C42-4D08-B3E5-7A21C6D9F480");

    /// <inheritdoc/>
    /// <remarks>
    /// Never placed by hand: a harness grows one of these per Receiver inside it, and one standing
    /// alone on the canvas would belong to nothing.
    /// </remarks>
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    /// <summary>
    /// Gets or sets the InstanceGuid of the Receiver this input feeds, or <see cref="Guid.Empty"/>
    /// for an input whose Receiver has gone (which the next sync removes).
    /// </summary>
    public Guid ReceiverId { get; set; }

    /// <summary>
    /// Gets or sets what to do when the user renames this input on the proxy: rename the Receiver it
    /// belongs to. Set by the harness; null on a parameter that has not been bound yet, and on one
    /// being rehydrated from an archive, where the name arriving IS the Receiver's own.
    /// </summary>
    internal Action<string>? Renamed { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The input and its Receiver share ONE name, and this is the half that carries an edit inward.
    /// Renaming the input on the proxy renames the Receiver inside the harness, exactly as renaming
    /// the Receiver relabels the input — either end can be edited and the other follows.
    ///
    /// <para>Overriding the property is the only way to see it happen. Grasshopper's own setter (on
    /// <c>GH_InstanceDescription</c>, and virtual, which is what makes this possible) raises no event
    /// at all, so there is nothing to subscribe to; it is virtual, so there is something to override.
    /// The recursion the two halves would otherwise make is cut by the equality guard: the Receiver
    /// pushes the very name that arrived here, so the second pass finds nothing to change.</para>
    /// </remarks>
    public override string NickName
    {
        get => base.NickName;

        set
        {
            if (string.Equals(base.NickName, value, StringComparison.Ordinal))
            {
                return;
            }

            base.NickName = value;
            Renamed?.Invoke(value);
        }
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetGuid(ReceiverKey, ReceiverId);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        ReceiverId = reader.ItemExists(ReceiverKey) ? reader.GetGuid(ReceiverKey) : Guid.Empty;
        return base.Read(reader);
    }
}
