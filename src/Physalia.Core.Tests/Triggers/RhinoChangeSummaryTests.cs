// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Triggers;
using Xunit;

namespace Physalia.Core.Tests.Triggers;

public class RhinoChangeSummaryTests
{
    [Fact]
    public void NamesEveryKindInTheBurst()
    {
        string text = RhinoChangeSummary.Describe(
            RhinoChangeKind.Geometry | RhinoChangeKind.Layers,
            412,
            0);

        Assert.Contains("geometry was edited", text);
        Assert.Contains("the layer table changed", text);
    }

    [Fact]
    public void CarriesTheCounts_BecauseASelectionWakeIsUnactionableWithoutThem()
    {
        string text = RhinoChangeSummary.Describe(RhinoChangeKind.Selection, 412, 3);

        Assert.Contains("412 objects", text);
        Assert.Contains("3 selected", text);
    }

    [Fact]
    public void SingleObjectReadsAsSingular()
    {
        Assert.Contains("1 object in the document", RhinoChangeSummary.Describe(RhinoChangeKind.Geometry, 1, 0));
    }

    [Fact]
    public void EmptyKinds_StillProducesAPayload_SoNoSignalIsBlank()
    {
        string text = RhinoChangeSummary.Describe(RhinoChangeKind.None, 0, 0);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("The Rhino document changed.", text);
    }
}
