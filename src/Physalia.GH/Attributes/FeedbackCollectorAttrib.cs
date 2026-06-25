// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the FeedbackCollector component. Renders a bottom-centre grip as the
/// visual landing target for bezier wires arriving from paired Feedback components. All the
/// behaviour — the collapse guard, the downward grip expansion, and drawing the grip — comes from
/// <see cref="BottomGripAttributes"/>; the collector adds no drag of its own.
/// </summary>
public class FeedbackCollectorAttrib : BottomGripAttributes
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FeedbackCollectorAttrib"/> class.
    /// </summary>
    /// <param name="feedbackCollector">The FeedbackCollector component that owns these attributes.</param>
    public FeedbackCollectorAttrib(FeedbackCollector feedbackCollector)
        : base(feedbackCollector)
    {
    }
}
