// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel;

namespace Physalia.GH.Components;

/// <summary>
/// A trigger that can be switched on and off from outside itself. Non-generic so the registry can
/// hold every kind of source in one list, whatever event type it deals in.
/// </summary>
public interface IArmableTrigger
{
    /// <summary>Gets a value indicating whether this trigger is currently listening.</summary>
    bool IsArmed { get; }

    /// <summary>Gets the trigger's nickname, for naming it in a panel or a menu.</summary>
    string NickName { get; }

    /// <summary>Gets the trigger's display name — what kind of trigger it is.</summary>
    string Name { get; }

    /// <summary>Gets the trigger's instance id, which is how a UI addresses one.</summary>
    Guid InstanceGuid { get; }

    /// <summary>Gets the caption the trigger is showing, so a UI can say what it is waiting for.</summary>
    string Message { get; }

    /// <summary>
    /// Gets a value indicating whether switching this trigger OFF produces a signal.
    ///
    /// <para>True for a recorder, where the batch is the result and disarming is the hand-over
    /// gesture. A UI offering a switch has to know: for these, "off" is not merely "stop", and
    /// telling somebody after the fact that their recording went nowhere is not good enough.</para>
    /// </summary>
    bool HandsOverOnDisarm { get; }

    /// <summary>
    /// Arms or disarms the trigger, DROPPING anything pending. The kill-switch verb: use it for
    /// "switch everything off", where nobody is asking for a round to start.
    /// </summary>
    /// <param name="on">True to start listening; false to stop.</param>
    void SetArmed(bool on);

    /// <summary>
    /// Arms or disarms the trigger exactly as its own right-click menu does — so disarming a recorder
    /// HANDS THE BATCH OVER rather than discarding it.
    ///
    /// <para>The verb for a switch aimed at one named trigger, which is a deliberate act on that
    /// trigger and should mean what the node's own menu means. <see cref="SetArmed"/> stays the verb
    /// for switching everything off at once.</para>
    /// </summary>
    /// <param name="on">True to start listening; false to stop and hand over.</param>
    void SetArmedAndHandOver(bool on);

    /// <summary>
    /// Resolves the document this trigger sits on, so a caller can ask about one pipeline's triggers
    /// rather than every trigger in Rhino.
    /// </summary>
    /// <returns>The document, or null when the trigger is not on one.</returns>
    GH_Document? OnPingDocument();
}

/// <summary>
/// Every trigger currently on any document, so something outside them can ask how many are armed and
/// switch them all off.
///
/// <para><b>Why a kill switch is part of this feature rather than an extra.</b> An armed trigger is
/// the one thing in the plug-in that spends money with nobody present, and the node it lives on may
/// be inside a harness the user is not looking at — possibly one of several. "Where is the thing that
/// keeps calling the model" is a question that needs an answer that is not "go and find it", so the
/// harness panel carries a disarm-everything button whenever anything is armed, and this is what it
/// reads.</para>
///
/// <para>Entries are held WEAKLY. A trigger unregisters itself on removal, but a document closed
/// wholesale disposes its objects without that always running, and a static list of strong references
/// to Grasshopper components is a leak that grows for the life of the Rhino session.</para>
/// </summary>
internal static class TriggerRegistry
{
    private static readonly object Gate = new();

    // ConditionalWeakTable keyed by the trigger itself: membership is the fact being stored, and the
    // table drops an entry when its key is collected. Same device HarnessComponent uses for ownership.
    private static readonly ConditionalWeakTable<IArmableTrigger, object> Known = new();

    // The table is not enumerable, so a parallel weak list carries the order. Dead entries are swept
    // whenever the list is read, which is often enough (a panel refresh) to keep it from growing.
    private static readonly List<WeakReference<IArmableTrigger>> Order = new();

    /// <summary>
    /// Records a trigger. Idempotent — a component reaches its document by several paths and may be
    /// registered more than once.
    /// </summary>
    /// <param name="trigger">The trigger to record.</param>
    internal static void Register(IArmableTrigger trigger)
    {
        if (trigger is null)
        {
            return;
        }

        lock (Gate)
        {
            if (Known.TryGetValue(trigger, out _))
            {
                return;
            }

            Known.Add(trigger, string.Empty);
            Order.Add(new WeakReference<IArmableTrigger>(trigger));
        }
    }

    /// <summary>
    /// Forgets a trigger.
    /// </summary>
    /// <param name="trigger">The trigger to forget.</param>
    internal static void Unregister(IArmableTrigger trigger)
    {
        if (trigger is null)
        {
            return;
        }

        lock (Gate)
        {
            Known.Remove(trigger);
            Order.RemoveAll(w => !w.TryGetTarget(out IArmableTrigger? t) || ReferenceEquals(t, trigger));
        }
    }

    /// <summary>
    /// Every trigger on one document, or on every document, armed or not.
    ///
    /// <para>A UI offering switches needs the whole set — a trigger that is off is precisely the one
    /// somebody came to switch on.</para>
    /// </summary>
    /// <param name="document">Restrict to this document, or null for all of them.</param>
    /// <returns>The triggers, in no particular order.</returns>
    internal static IReadOnlyList<IArmableTrigger> All(GH_Document? document) =>
        Live()
            .Where(t => document is null || ReferenceEquals(t.OnPingDocument(), document))
            .ToList();

    /// <summary>
    /// The armed triggers on one document, or on every document.
    /// </summary>
    /// <param name="document">Restrict to this document, or null for all of them.</param>
    /// <returns>The armed triggers.</returns>
    internal static IReadOnlyList<IArmableTrigger> Armed(GH_Document? document = null)
    {
        return Live()
            .Where(t => t.IsArmed)
            .Where(t => document is null || ReferenceEquals(t.OnPingDocument(), document))
            .ToList();
    }

    /// <summary>
    /// Switches off every armed trigger on one document, or on every document.
    /// </summary>
    /// <param name="document">Restrict to this document, or null for all of them.</param>
    /// <returns>How many were switched off.</returns>
    internal static int DisarmAll(GH_Document? document = null)
    {
        IReadOnlyList<IArmableTrigger> armed = Armed(document);

        foreach (IArmableTrigger trigger in armed)
        {
            trigger.SetArmed(false);
        }

        return armed.Count;
    }

    private static IReadOnlyList<IArmableTrigger> Live()
    {
        lock (Gate)
        {
            var live = new List<IArmableTrigger>(Order.Count);
            Order.RemoveAll(w =>
            {
                if (w.TryGetTarget(out IArmableTrigger? trigger))
                {
                    live.Add(trigger);
                    return false;
                }

                return true;
            });

            return live;
        }
    }
}
