// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;

namespace Physalia.GH.Components;

/// <summary>
/// On receiving a signal, scans the active Grasshopper document for components reporting
/// errors and routes the outcome: a clean canvas passes the incoming payload forward on the
/// Success Signal; any error-level runtime messages are gathered into a report routed back on
/// the Fail Signal. Wiring it after the Component Transmitter (and its Fail Signal back through
/// Feedback to the Recorder) turns the place → read → correct loop into a visible cycle on the
/// canvas rather than logic hidden inside one component.
/// </summary>
public class Observer : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Observer"/> class.
    /// </summary>
    public Observer()
        : base("Observer", "Obs", "Scans the active document for components reporting errors and routes a report back on the Fail Signal; a clean canvas passes the signal through.", "Regulators")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F4B0D63C-8E57-4A4B-9C3D-5B2A7F1E04D8");

    /// <inheritdoc/>
    /// <remarks>The payload (typically the placed GhJSON) is passed through unchanged when the canvas is clean.</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — the upstream placement settles before its Success Signal arrives.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        // Intentionally empty: observation only reads, it has nothing to push.
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        List<string> problems = CollectDocumentErrors();

        return problems.Count == 0
            ? RoutingResult.Ok(data)
            : RoutingResult.Fail(BuildFeedback(problems), $"{problems.Count} component(s) on the canvas reported errors.", GH_RuntimeMessageLevel.Warning);
    }

    /// <summary>
    /// Gathers error-level runtime messages from every active object on the document except
    /// this Observer itself.
    /// </summary>
    /// <returns>One entry per error, prefixed with the reporting object's name.</returns>
    private List<string> CollectDocumentErrors()
    {
        var problems = new List<string>();
        GH_Document? doc = OnPingDocument();
        if (doc is null)
        {
            return problems;
        }

        foreach (IGH_DocumentObject obj in doc.Objects)
        {
            if (obj.InstanceGuid == InstanceGuid || obj is not IGH_ActiveObject ao)
            {
                continue;
            }

            foreach (string message in ao.RuntimeMessages(GH_RuntimeMessageLevel.Error))
            {
                problems.Add($"{ao.Name}: {message}");
            }
        }

        return problems;
    }

    private static string BuildFeedback(IReadOnlyList<string> problems)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Components on the canvas are reporting errors. Please correct the definition and resubmit.");
        sb.AppendLine();
        sb.AppendLine("Errors:");
        foreach (string problem in problems)
        {
            sb.AppendLine($"  - {problem}");
        }

        return sb.ToString().TrimEnd();
    }
}
