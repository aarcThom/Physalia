// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;

namespace Physalia.GH.Components;

/// <summary>
/// On receiving a signal, scans Grasshopper components for runtime problems and routes the
/// outcome: a clean scan passes the incoming payload forward on the Success Signal; any errors,
/// warnings, or dead components (ones that produced no output) are gathered into a report routed
/// back on the Fail Signal.
///
/// <para>The scan is scoped by the incoming payload. A Component Transmitter forwards the GUIDs of
/// the components it placed (newline-separated) as its Success payload; Observer parses those and
/// scans <b>only</b> that placed graph, so it never flags idle pipeline components. When the
/// payload carries no GUIDs it falls back to scanning the whole document (errors/warnings only),
/// preserving use as a standalone canvas probe. Wiring it after the Component Transmitter (and its
/// Fail Signal back through Feedback to the Recorder) turns the place → read → correct loop into a
/// visible cycle on the canvas.</para>
/// </summary>
public class Observer : RoutingComponentBase<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Observer"/> class.
    /// </summary>
    public Observer()
        : base("Observer", "Obs", "Scans the placed graph (or whole document) for errors, warnings, and dead components and routes a report back on the Fail Signal; a clean scan passes the signal through.", "Regulators")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F4B0D63C-8E57-4A4B-9C3D-5B2A7F1E04D8");

    /// <inheritdoc/>
    /// <remarks>The payload (the placed components' GUIDs from a Component Transmitter) is passed through unchanged when the scan is clean.</remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>Synchronous component — observation only reads, it has nothing to push.</remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Defers the scan until every scoped component has finished solving, so the runtime state
    /// read in <see cref="ReadSolve"/> reflects the settled graph rather than a still-expired one.
    /// </remarks>
    protected override bool IsReadReady(string data)
    {
        GH_Document? doc = OnPingDocument();
        if (doc is null)
        {
            return true;
        }

        foreach (IGH_DocumentObject obj in ScopedObjects(doc, data))
        {
            if (obj is IGH_ActiveObject ao &&
                (ao.Phase == GH_SolutionPhase.Blank || ao.Phase == GH_SolutionPhase.Computing))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var dead = new List<string>();

        GH_Document? doc = OnPingDocument();
        if (doc is not null)
        {
            foreach (IGH_DocumentObject obj in ScopedObjects(doc, data))
            {
                if (obj is not IGH_ActiveObject ao)
                {
                    continue;
                }

                var objErrors = ao.RuntimeMessages(GH_RuntimeMessageLevel.Error);
                foreach (string message in objErrors)
                {
                    errors.Add($"{ao.Name}: {message}");
                }

                foreach (string message in ao.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
                {
                    warnings.Add($"{ao.Name}: {message}");
                }

                // A component that produced no data on any output is "dead". Skip one that already
                // reported an error — its empty output is a cascade symptom, not the root cause.
                if (objErrors.Count == 0 &&
                    obj is IGH_Component comp &&
                    comp.Params.Output.Count > 0 &&
                    comp.Params.Output.All(p => p.VolatileData.IsEmpty))
                {
                    dead.Add(comp.Name);
                }
            }
        }

        int total = errors.Count + warnings.Count + dead.Count;
        return total == 0
            ? RoutingResult.Ok(data)
            : RoutingResult.Fail(BuildFeedback(errors, warnings, dead), $"{total} problem(s) found in the scanned graph.", GH_RuntimeMessageLevel.Warning);
    }

    /// <summary>
    /// The objects to scan: the components named by the incoming GUID payload, or every object on
    /// the document (except this Observer) when the payload carries no parseable GUIDs.
    /// </summary>
    /// <param name="doc">The active document.</param>
    /// <param name="payload">The incoming signal payload (newline-separated GUIDs, or anything else).</param>
    /// <returns>The objects in scope for the scan.</returns>
    private IEnumerable<IGH_DocumentObject> ScopedObjects(GH_Document doc, string payload)
    {
        var scoped = ParseGuids(payload)
            .Select(g => doc.FindObject(g, false))
            .Where(o => o is not null)
            .Select(o => o!)
            .ToList();

        if (scoped.Count > 0)
        {
            return scoped;
        }

        // No GUIDs in the payload: fall back to a whole-document scan as a standalone probe.
        return doc.Objects.Where(o => o.InstanceGuid != InstanceGuid);
    }

    /// <summary>
    /// Parses newline-separated GUIDs from the payload, skipping any line that is not a GUID.
    /// </summary>
    /// <param name="payload">The incoming signal payload.</param>
    /// <returns>The GUIDs found in the payload.</returns>
    private static IEnumerable<Guid> ParseGuids(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        foreach (string line in payload.Split('\n'))
        {
            if (Guid.TryParse(line.Trim(), out Guid guid))
            {
                yield return guid;
            }
        }
    }

    private static string BuildFeedback(IReadOnlyList<string> errors, IReadOnlyList<string> warnings, IReadOnlyList<string> dead)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The placed graph reported problems. Please correct the definition and resubmit.");

        AppendSection(sb, "Errors:", errors);
        AppendSection(sb, "Warnings:", warnings);
        AppendSection(sb, "Components that produced no output (check their inputs and upstream wiring):", dead);

        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine(heading);
        foreach (string item in items)
        {
            sb.AppendLine($"  - {item}");
        }
    }
}
