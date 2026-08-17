// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Grounding;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class CanvasStateGroundingTests
{
    private const string Json = "{\"schema\":\"1.0\",\"components\":[{\"name\":\"Circle\",\"id\":1}]}";
    private const string Checksum = "sha256-abc123";

    [Fact]
    public void ToSystemPromptSection_WithState_RendersJsonAndChecksum()
    {
        string section = new CanvasStateGrounding(Json, Checksum, 1).ToSystemPromptSection();

        Assert.Contains("CURRENT state of the Grasshopper canvas", section);
        Assert.Contains(Json, section);
        Assert.Contains("patch.base.checksum: " + Checksum, section);
        Assert.Contains("ghpatch", section);
        Assert.Contains("physalia.rhinoRef", section);
    }

    [Fact]
    public void ToSystemPromptSection_EmptyCanvas_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new CanvasStateGrounding(string.Empty, string.Empty, 0).ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_ZeroComponents_ReturnsEmptyEvenWithText()
    {
        // A zero-count snapshot must contribute nothing, so the model falls back to emitting a
        // full document rather than patching an empty canvas.
        Assert.Equal(string.Empty, new CanvasStateGrounding(Json, Checksum, 0).ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_BlankJson_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new CanvasStateGrounding("   ", Checksum, 3).ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_BlankChecksum_OmitsChecksumLine()
    {
        string section = new CanvasStateGrounding(Json, string.Empty, 1).ToSystemPromptSection();

        Assert.Contains(Json, section);
        Assert.DoesNotContain("checksum", section, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSystemPromptSection_NoModelPlaced_StatesNoneCameFromModel()
    {
        // Default ModelPlacedCount = 0: the provenance line must say outright that nothing on the
        // canvas is the model's, steering a new build to a full document. Stated, not inferred —
        // inferring placement status is what makes corrective turns wobble between modes.
        string section = new CanvasStateGrounding(Json, Checksum, 1).ToSystemPromptSection();

        Assert.Contains("Provenance: NONE of these components came from you", section);
        Assert.Contains("full GhJSON document", section);
    }

    [Fact]
    public void ToSystemPromptSection_ModelPlaced_StatesCountAndPatchMode()
    {
        string section = new CanvasStateGrounding(Json, Checksum, 5, 3).ToSystemPromptSection();

        Assert.Contains("Provenance: 3 of these components were placed from your previous responses", section);
        Assert.Contains("edit it via ghpatch", section);
    }

    [Fact]
    public void ToSystemPromptSection_GroupScoped_StatesVisibilityContract()
    {
        // The scoped frame must say outright that this is the model's WHOLE view (auto-enrollment,
        // hidden canvas, user opts components in) so it neither reasons about hidden components nor
        // wonders where its placed graph went.
        string section = new CanvasStateGrounding(Json, Checksum, 1) { GroupScoped = true }.ToSystemPromptSection();

        // "your Physalia group", not "the 'Physalia' group": a canvas can carry one per pipeline.
        Assert.Contains("your Physalia group", section);
        Assert.Contains("added to this group automatically", section);
        Assert.Contains("hidden", section);
        Assert.DoesNotContain("CURRENT state of the Grasshopper canvas", section);
        Assert.Contains(Json, section);
        Assert.Contains("patch.base.checksum: " + Checksum, section);
    }

    [Fact]
    public void ToSystemPromptSection_GroupScopedProvenance_NamesTheGroup()
    {
        string section = new CanvasStateGrounding(Json, Checksum, 1) { GroupScoped = true }.ToSystemPromptSection();

        Assert.Contains("the group holds only the user's own work", section);
    }
}
