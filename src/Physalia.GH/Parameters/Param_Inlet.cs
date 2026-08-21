// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;

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
///
/// <para>Its nickname is shared with the Receiver's own output, through
/// <see cref="Param_LinkedName"/>: the two are ONE name, both starting out "Data", and renaming
/// either end renames the other.</para>
/// </summary>
public class Param_Inlet : Param_LinkedName
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
