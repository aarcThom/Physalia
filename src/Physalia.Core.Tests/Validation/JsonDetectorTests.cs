// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Validation;
using Xunit;

namespace Physalia.Core.Tests.Validation;

public class JsonDetectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ContainsJson_BlankInput_ReturnsFalse(string? text)
    {
        Assert.False(JsonDetector.ContainsJson(text!));
    }

    [Fact]
    public void ContainsJson_PlainProse_ReturnsFalse()
    {
        Assert.False(JsonDetector.ContainsJson("Sure, I can help with that. What would you like to build?"));
    }

    [Fact]
    public void ContainsJson_ChatGreeting_ReturnsFalse()
    {
        string text = "Hello! I'm a computational designer who builds Grasshopper definitions from node graphs.\n" +
                      "Let me know what you need, and I'll design the definition with native Grasshopper components. " +
                      "What would you like me to build?";

        Assert.False(JsonDetector.ContainsJson(text));
    }

    [Fact]
    public void ContainsJson_IncidentalBraces_ReturnsFalse()
    {
        Assert.False(JsonDetector.ContainsJson("Use {placeholder} in your template and [brackets] for lists."));
    }

    [Fact]
    public void ContainsJson_WellFormedObject_ReturnsTrue()
    {
        Assert.True(JsonDetector.ContainsJson("{\"a\":1,\"b\":2}"));
    }

    [Fact]
    public void ContainsJson_ObjectEmbeddedInProse_ReturnsTrue()
    {
        Assert.True(JsonDetector.ContainsJson("Sure, here is the result {\"a\":1} and that's it."));
    }

    [Fact]
    public void ContainsJson_TruncatedJson_ReturnsTrue()
    {
        Assert.True(JsonDetector.ContainsJson("{\"a\": [1, 2"));
    }

    [Fact]
    public void ContainsJson_TruncatedJsonInProse_ReturnsTrue()
    {
        Assert.True(JsonDetector.ContainsJson("Here you go: {\"components\": ["));
    }

    [Fact]
    public void ContainsJson_JsonFence_ReturnsTrue()
    {
        Assert.True(JsonDetector.ContainsJson("Here you go:\n```json\n{\"a\":1}\n```\nThanks."));
    }

    [Fact]
    public void ContainsJson_JsonFenceWithGarbageContents_ReturnsTrue()
    {
        // A ```json fence is a declaration of intent — pass it through and let the Auditor judge it.
        Assert.True(JsonDetector.ContainsJson("```json\nnot really json\n```"));
    }

    [Fact]
    public void ContainsJson_StartsWithArray_ReturnsTrue()
    {
        Assert.True(JsonDetector.ContainsJson("  [1, 2, 3]"));
    }

    [Fact]
    public void ContainsJson_StartsWithBrace_ReturnsTrue()
    {
        Assert.True(JsonDetector.ContainsJson("\n{\"pins\": {}"));
    }

    [Fact]
    public void ContainsJson_GenericFenceWithDictLiteral_ReturnsTrue()
    {
        // Accepted pass-through bias: a Python dict has the same key signature as JSON. Forwarding
        // it reproduces today's behavior (Auditor rejects it) rather than risking a false negative.
        Assert.True(JsonDetector.ContainsJson("Try this:\n```\nconfig = {\"a\": 1}\n```"));
    }

    [Fact]
    public void ContainsJson_KeySignatureBeforeAnyBrace_ReturnsFalse()
    {
        // The signature must follow the first opening brace/bracket to count as structure.
        Assert.False(JsonDetector.ContainsJson("The \"components\": field is missing from your file."));
    }
}
