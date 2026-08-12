// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// Base for signal-driven transmitters that push into an EXISTING component on the user's canvas — a
/// script component of one language or another — rather than placing new ones. Being linked is
/// delegated to a <see cref="TransmitterLink"/>: the target's id and persistence, the link/unlink
/// gestures, the settled wire, and resolving the id back to a live object.
///
/// <para>A subclass says only what counts as a valid target (<see cref="IsLinkTarget"/>), what to
/// call that kind of component in a message (<see cref="TargetKind"/>), and what pushing means. That
/// is the seam the Python, IronPython and C# transmitters differ across. A transmitter that is NOT
/// signal-driven composes the same <see cref="TransmitterLink"/> directly instead — see
/// <see cref="TextTransmitter"/>.</para>
/// </summary>
public abstract class ScriptTransmitterBase : TransmitterComponentBase, IGuidLinked
{
    private TransmitterLink? _link;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptTransmitterBase"/> class.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected ScriptTransmitterBase(string name, string nickname, string description)
        : base(name, nickname, description)
    {
    }

    /// <summary>
    /// Gets the InstanceGuid of the linked target component, or <see cref="Guid.Empty"/> if unlinked.
    /// </summary>
    public Guid LinkedGuid => Link.Guid;

    /// <summary>
    /// Gets how a locked interface is described to the model for this transmitter's language — read
    /// by the <see cref="ScriptIO"/> that links to it as well as by its own rejection feedback,
    /// so both speak of the same schema and the same code rule.
    /// </summary>
    public abstract ScriptInterfaceDialect Dialect { get; }

    /// <summary>
    /// Gets what this transmitter's target is called in messages to the user and the model —
    /// "Python Script", "C# Script", and so on.
    /// </summary>
    protected abstract string TargetKind { get; }

    /// <summary>
    /// Gets whether a locked submission may declare FEWER parameters than the target actually has.
    /// True where the code only reads the names it declares (Python), false where the code restates
    /// the whole interface and an undeclared parameter has nothing to bind to (C#).
    /// </summary>
    protected virtual bool AllowsPartialInterface => true;

    /// <summary>
    /// Gets the <see cref="ScriptIO"/> actively locking this transmitter's interface: an enabled one
    /// anywhere in this document whose grip link points here. Null when the interface is free to
    /// restructure.
    /// </summary>
    protected ScriptIO? ActiveScriptIO
        => OnPingDocument()?.Objects.OfType<ScriptIO>().FirstOrDefault(l => l.Constrains(InstanceGuid));

    // Built on first use rather than in the constructor: it is wired from TargetKind and
    // IsLinkTarget, which a subclass overrides.
    private TransmitterLink Link
    {
        get
        {
            if (_link is null)
            {
                _link = new TransmitterLink(this, "Script Component", TargetKind, IsLinkTarget);
                _link.Changed = OnLinkChanged;
            }

            return _link;
        }
    }

    /// <summary>
    /// Whether a document object on the host canvas is a component this transmitter can push into.
    /// </summary>
    /// <param name="candidate">An object on the user's canvas.</param>
    /// <returns>true when it is a valid link target.</returns>
    protected abstract bool IsLinkTarget(IGH_DocumentObject candidate);

    /// <summary>
    /// Links this component to a target component.
    /// Called from the right-click picker, and by the harness proxy's delegated arrow on drop.
    /// </summary>
    /// <param name="guid">The InstanceGuid of the component to link.</param>
    public void LinkTo(Guid guid) => Link.Assign(guid);

    /// <summary>
    /// Removes the current link. Does not modify the previously-linked component's code.
    /// Called from the right-click picker, and by the harness proxy's delegated arrow on Ctrl+drop.
    /// </summary>
    public void Unlink() => Link.Assign(Guid.Empty);

    /// <inheritdoc/>
    /// <remarks>The wire lands just under the linked component.</remarks>
    public override IEnumerable<PointF> GetArrowEndpoints(GH_Document hostDocument) =>
        Link.Endpoints(hostDocument);

    /// <inheritdoc/>
    /// <remarks>Links the component under the drop point; Ctrl unlinks instead.</remarks>
    public override void HandleDrop(GH_Document hostDocument, PointF dropPoint, bool ctrl) =>
        Link.HandleDrop(hostDocument, dropPoint, ctrl);

    /// <inheritdoc/>
    /// <remarks>Offers the grip link as a menu, for when the target cannot be reached by a drag.</remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        Link.AppendMenuItems(menu);
    }

    /// <inheritdoc/>
    void IGuidLinked.RemapLinks(IReadOnlyDictionary<Guid, Guid> replacements) => Link.Remap(replacements);

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        Link.Write(writer);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        Link.Read(reader);
        return base.Read(reader);
    }

    /// <summary>
    /// Applies a link change from the menu or a drop, with undo and a re-solve.
    /// </summary>
    /// <param name="guid">The target to link, or <see cref="Guid.Empty"/> to unlink.</param>
    protected void SetLink(Guid guid) => Link.Set(guid);

    /// <summary>
    /// Resolves the linked component on the host canvas, or returns null with a message when no
    /// valid target is linked.
    /// </summary>
    /// <param name="error">A human-readable reason when resolution fails; otherwise null.</param>
    /// <returns>The linked document object, or null.</returns>
    protected IGH_DocumentObject? ResolveTarget(out string? error) => Link.Resolve(out error);

    /// <summary>
    /// Called after the link is repointed. Override to drop any state that was about the OLD target —
    /// a transmitter that skips a push because "the target already has this" must forget that, or the
    /// newly linked component is left empty. Does nothing by default.
    /// </summary>
    protected virtual void OnLinkChanged()
    {
    }

    /// <summary>
    /// Validates a submission against the target's live (locked) interface: every declared input and
    /// output name must already exist on the target, and — where the language restates the interface
    /// in its code (<see cref="AllowsPartialInterface"/>) — every existing name must be declared.
    /// An unknown name means the model tried to add or rename a parameter, which would break the
    /// wires the lock exists to protect.
    /// </summary>
    /// <param name="target">The linked script component.</param>
    /// <param name="inputs">The submission's declared input specs.</param>
    /// <param name="outputs">The submission's declared output specs.</param>
    /// <param name="feedback">Corrective feedback for the model when validation fails.</param>
    /// <returns>true when the submission respects the locked interface.</returns>
    protected bool RespectsLockedInterface(
        IGH_DocumentObject target,
        IReadOnlyList<GhParamSpec> inputs,
        IReadOnlyList<GhParamSpec> outputs,
        out string feedback)
    {
        feedback = string.Empty;

        IReadOnlyList<GhParamSpec> lockedInputs = GhPythonBridge.GetInputSpecs(target);
        IReadOnlyList<GhParamSpec> lockedOutputs = GhPythonBridge.GetOutputSpecs(target);

        var problems = new List<string>();
        CompareToLock(problems, "input", lockedInputs, inputs);
        CompareToLock(problems, "output", lockedOutputs, outputs);

        if (problems.Count == 0)
        {
            return true;
        }

        feedback = BuildLockFeedback(target.NickName, lockedInputs, lockedOutputs, problems);
        return false;
    }

    /// <summary>
    /// Records how one side of a submission departs from the locked set: names the target does not
    /// have, and — only where the language forbids a partial declaration — locked names the
    /// submission left out.
    /// </summary>
    /// <param name="problems">The running list of problems.</param>
    /// <param name="kind">"input" or "output", for the message.</param>
    /// <param name="locked">The target's live specs.</param>
    /// <param name="declared">The submission's declared specs.</param>
    private void CompareToLock(
        List<string> problems,
        string kind,
        IReadOnlyList<GhParamSpec> locked,
        IReadOnlyList<GhParamSpec> declared)
    {
        var lockedNames = new HashSet<string>(locked.Select(p => p.Name), StringComparer.Ordinal);
        var declaredNames = new HashSet<string>(declared.Select(p => p.Name), StringComparer.Ordinal);

        List<string> unknown = declaredNames.Where(n => !lockedNames.Contains(n)).ToList();
        if (unknown.Count > 0)
        {
            problems.Add($"{kind}s the component does not have: {string.Join(", ", unknown)}");
        }

        if (AllowsPartialInterface)
        {
            return;
        }

        List<string> undeclared = lockedNames.Where(n => !declaredNames.Contains(n)).ToList();
        if (undeclared.Count > 0)
        {
            problems.Add($"locked {kind}s you left out: {string.Join(", ", undeclared)}");
        }
    }

    /// <summary>
    /// Builds the corrective feedback for a locked-interface violation: what was rejected and why,
    /// then the locked contract rendered exactly as the grounding renders it, so the model sees the
    /// same JSON entries it must copy on the resubmission.
    /// </summary>
    /// <param name="componentName">The target script component's display name.</param>
    /// <param name="lockedInputs">The target's live input specs.</param>
    /// <param name="lockedOutputs">The target's live output specs.</param>
    /// <param name="problems">How the submission departed from the locked set.</param>
    /// <returns>The feedback text.</returns>
    private string BuildLockFeedback(
        string componentName,
        IReadOnlyList<GhParamSpec> lockedInputs,
        IReadOnlyList<GhParamSpec> lockedOutputs,
        IReadOnlyList<string> problems)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Your submission was REJECTED and nothing was applied: it does not declare the locked component's parameters.");
        foreach (string problem in problems)
        {
            sb.AppendLine($"  - {problem}");
        }

        sb.AppendLine();

        var contract = new ScriptInterfaceGrounding(
            componentName,
            ScriptIO.ToPorts(lockedInputs),
            ScriptIO.ToPorts(lockedOutputs),
            Dialect);
        sb.AppendLine(contract.ToSystemPromptSection());
        sb.AppendLine();
        sb.Append($"Resubmit the full {Dialect.SchemaName} JSON declaring exactly these parameters. {Dialect.CodeRule}");

        return sb.ToString();
    }
}
