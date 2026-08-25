// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="ReadPdfTool"/>, which switches PDF intake on in the chat window: a button
/// that opens a file picker, and drag-and-drop onto the prompt box. Without it wired, a dropped PDF
/// is refused. Has no inputs.
///
/// <para>This is the HUMAN half of a pair and does nothing on its own. It gets a file into the
/// conversation; the model-callable <see cref="ReadPdf"/> component — wired to a Router, under LLM
/// Tools — is what reads one. Attaching a PDF here spends almost nothing: the turn carries a short
/// descriptor and no page content, and the model pulls text or a rendered page only when it needs
/// one.</para>
///
/// <para>The class is called <c>AddPdf</c> and displayed as "Read PDF" because both halves of the
/// pair carry that name on the ribbon, in different sections, while every GH component here shares
/// one flat namespace and resolves its icon by type name — so the two types cannot both be called
/// <c>ReadPdf</c>. The name a user sees is the one that matters; the type name only has to be
/// distinct.</para>
/// </summary>
public class AddPdf : HumanToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddPdf"/> class.
    /// </summary>
    public AddPdf()
        : base(
            "Read PDF",
            "ReadPDF",
            "Lets you put PDFs into the prompt box — drag and drop, or pick a file. Attaching one " +
            "costs almost nothing: the conversation gets a short summary, and the model reads pages " +
            "on demand through the Read PDF tool under LLM Tools. Without this component, PDF " +
            "attachments are off.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6E1B4A73-0C58-4D92-BF37-2A9D5E8C4106");

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Switches PDF attachments on in the chat window. Wire into a Conversation Log's Human " +
        "Tools input. Pair it with the Read PDF tool under LLM Tools, wired to a Router, so the " +
        "model can actually read what you attach.";

    /// <inheritdoc/>
    protected override HumanTool Tool => new ReadPdfTool();
}
