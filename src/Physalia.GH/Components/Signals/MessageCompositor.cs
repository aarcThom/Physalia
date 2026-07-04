// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Composes a role string and a content string into a <see cref="ConversationMessage"/>.
/// </summary>
public class MessageCompositor : PhyBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageCompositor"/> class.
    /// </summary>
    public MessageCompositor()
        : base("Message Compositor", "MComp", "Composes a role and content string into a ConversationMessage.", "Signals")
    {
    }

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.senary;

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("78E35C3F-43DC-4A9C-811B-B560FA6D1245");

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Role", "R", "The role for this turn: User or Assistant.", GH_ParamAccess.item, "User");
        pManager.AddTextParameter("Content", "C", "The text content for this turn.", GH_ParamAccess.item, string.Empty);
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_ConversationMessage(), "Message", "M", "The composed conversation message.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string roleStr = "User";
        string content = string.Empty;

        DA.GetData(0, ref roleStr);
        DA.GetData(1, ref content);

        if (!Enum.TryParse<Role>(roleStr, ignoreCase: true, out var role))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Invalid role '{roleStr}'. Expected 'User' or 'Assistant'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Content is empty.");
            return;
        }

        var message = new ConversationMessage(role, content);
        DA.SetData(0, new GH_ConversationMessage(message));
    }
}
