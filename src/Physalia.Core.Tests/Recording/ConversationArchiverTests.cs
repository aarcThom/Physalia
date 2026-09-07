// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Recording;
using Xunit;

namespace Physalia.Core.Tests.Recording;

public class ConversationArchiverTests
{
    private static Conversation Build(params ConversationMessage[] messages)
    {
        Conversation conversation = Conversation.Empty;
        foreach (ConversationMessage message in messages)
        {
            conversation = conversation.Append(message);
        }

        return conversation;
    }

    private static Conversation RoundTrip(Conversation source, out IReadOnlyList<ArchivedImage> images)
    {
        ConversationArchive archive = ConversationArchiver.Write(source);
        images = archive.Images;

        Dictionary<string, ArchivedImage> store = archive.Images.ToDictionary(i => i.Key);

        Result<Conversation, string> read = ConversationArchiver.Read(
            archive.Json,
            key => store.TryGetValue(key, out ArchivedImage? found) ? found : null);

        Assert.True(read.IsOk(out Conversation? result, out string? error), error);
        return result!;
    }

    [Fact]
    public void EmptyConversation_RoundTripsToEmpty()
    {
        Conversation result = RoundTrip(Conversation.Empty, out _);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void TextTurnsRoundTripWithTheirRoles()
    {
        Conversation source = Build(
            new ConversationMessage(Role.User, "design a canopy"),
            new ConversationMessage(Role.Assistant, "how wide?"));

        Conversation result = RoundTrip(source, out _);

        Assert.Equal(2, result.Count);
        Assert.Equal(Role.User, result.Messages[0].Role);
        Assert.Equal(Role.Assistant, result.Messages[1].Role);
        Assert.Equal("design a canopy", Assert.IsType<TextContent>(result.Messages[0].Content[0]).Text);
    }

    [Fact]
    public void InlineImagesAreLiftedOutAsFilesAndPutBack()
    {
        byte[] bytes = { 1, 2, 3, 4 };
        Conversation source = Build(
            new ConversationMessage(Role.User, new MessageContent[]
            {
                new TextContent("look at this"),
                new ImageContent(new InlineImage(bytes, "image/png")),
            }));

        Conversation result = RoundTrip(source, out IReadOnlyList<ArchivedImage> images);

        // Beside the transcript, not inside it: base64 in JSON triples the bytes and makes a file
        // nobody can read by eye.
        ArchivedImage saved = Assert.Single(images);
        Assert.EndsWith(".png", saved.Key);
        Assert.Equal(bytes, saved.Bytes);

        var restored = Assert.IsType<ImageContent>(result.Messages[0].Content[1]);
        Assert.Equal(bytes, Assert.IsType<InlineImage>(restored.Source).Data);
    }

    [Fact]
    public void AMissingImageFileLosesTheBlockAndKeepsTheTurn()
    {
        Conversation source = Build(
            new ConversationMessage(Role.User, new MessageContent[]
            {
                new TextContent("the brief"),
                new ImageContent(new InlineImage(new byte[] { 9 }, "image/png")),
            }));

        ConversationArchive archive = ConversationArchiver.Write(source);

        // The loader finds nothing — someone deleted the images folder.
        Result<Conversation, string> read = ConversationArchiver.Read(archive.Json, _ => null);

        Assert.True(read.IsOk(out Conversation? result, out _));
        ConversationMessage turn = Assert.Single(result!.Messages);
        Assert.Equal("the brief", Assert.IsType<TextContent>(Assert.Single(turn.Content)).Text);
    }

    [Fact]
    public void ATurnLeftWithNoBlocksAtAllIsSkipped_BecauseProvidersRejectIt()
    {
        Conversation source = Build(
            new ConversationMessage(Role.User, new MessageContent[]
            {
                new ImageContent(new InlineImage(new byte[] { 9 }, "image/png")),
            }));

        Result<Conversation, string> read = ConversationArchiver.Read(
            ConversationArchiver.Write(source).Json,
            _ => null);

        Assert.True(read.IsOk(out Conversation? result, out _));
        Assert.Equal(0, result!.Count);
    }

    [Fact]
    public void UrlAndManagedImagesAreKeptAsReferences_NotFetched()
    {
        Conversation source = Build(
            new ConversationMessage(Role.User, new MessageContent[]
            {
                new ImageContent(new UrlImage("https://example.com/a.png")),
            }),
            new ConversationMessage(Role.Assistant, new MessageContent[]
            {
                new ImageContent(new ManagedImage("files/abc123")),
            }));

        Conversation result = RoundTrip(source, out IReadOnlyList<ArchivedImage> images);

        Assert.Empty(images);
        Assert.Equal(
            "https://example.com/a.png",
            Assert.IsType<UrlImage>(Assert.IsType<ImageContent>(result.Messages[0].Content[0]).Source).Url);
        Assert.Equal(
            "files/abc123",
            Assert.IsType<ManagedImage>(Assert.IsType<ImageContent>(result.Messages[1].Content[0]).Source).FileHandle);
    }

    [Fact]
    public void ToolCallsAndResultsRoundTrip()
    {
        Conversation source = Build(
            new ConversationMessage(Role.Assistant, new MessageContent[]
            {
                new ToolCallContent("call_1", "web_search", "{\"query\":\"x\"}"),
            }),
            new ConversationMessage(Role.User, new MessageContent[]
            {
                new ToolResultContent("call_1", "nothing found", true),
            }));

        Conversation result = RoundTrip(source, out _);

        var call = Assert.IsType<ToolCallContent>(result.Messages[0].Content[0]);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("web_search", call.Name);
        Assert.Equal("{\"query\":\"x\"}", call.InputJson);

        var answer = Assert.IsType<ToolResultContent>(result.Messages[1].Content[0]);
        Assert.Equal("call_1", answer.ToolCallId);
        Assert.True(answer.IsError);
    }

    [Fact]
    public void FeedbackFlagAndSourceAttributionSurvive()
    {
        // Attribution is what badges a feedback turn with the node that produced it. A resumed
        // conversation that lost it would show every automated turn as though a person had typed it.
        Guid id = Guid.NewGuid();
        Conversation source = Build(
            new ConversationMessage(Role.User, "fix this")
            {
                IsFeedback = true,
                Sources = new[] { new ComponentOrigin(id, "Geometry Report") },
            });

        Conversation result = RoundTrip(source, out _);

        ConversationMessage turn = Assert.Single(result.Messages);
        Assert.True(turn.IsFeedback);
        ComponentOrigin origin = Assert.Single(turn.Sources);
        Assert.Equal(id, origin.Id);
        Assert.Equal("Geometry Report", origin.Name);
    }

    [Fact]
    public void ANewerFormatIsRefused_NotGuessedAt()
    {
        string json = "{\"version\":99,\"turns\":[]}";

        Assert.True(ConversationArchiver.Read(json).IsErr(out string? error, out _));
        Assert.Contains("newer version", error);
    }

    [Fact]
    public void MalformedJsonIsReportedRatherThanThrowing()
    {
        Assert.True(ConversationArchiver.Read("{not json").IsErr(out string? error, out _));
        Assert.Contains("not valid JSON", error);
    }

    [Fact]
    public void ATurnWithNoReadableRoleIsRefused()
    {
        string json = "{\"version\":1,\"turns\":[{\"role\":\"system\",\"content\":[{\"type\":\"text\",\"text\":\"x\"}]}]}";

        Assert.True(ConversationArchiver.Read(json).IsErr(out string? error, out _));
        Assert.Contains("role", error);
    }

    [Fact]
    public void ConsecutiveSameRoleTurnsAreMergedRatherThanRefused()
    {
        // Conversation refuses these and so would every provider. A transcript is somebody's work:
        // reading it back as one longer turn beats reading nothing.
        string json = "{\"version\":1,\"turns\":["
            + "{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"one\"}]},"
            + "{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"two\"}]}]}";

        Assert.True(ConversationArchiver.Read(json).IsOk(out Conversation? result, out _));

        ConversationMessage turn = Assert.Single(result!.Messages);
        Assert.Contains("one", Assert.IsType<TextContent>(turn.Content[0]).Text);
        Assert.Contains("two", Assert.IsType<TextContent>(turn.Content[0]).Text);
    }

    [Fact]
    public void AnUnknownBlockTypeIsDroppedAndTheTurnKept()
    {
        string json = "{\"version\":1,\"turns\":[{\"role\":\"user\",\"content\":["
            + "{\"type\":\"somethingNew\",\"payload\":\"?\"},"
            + "{\"type\":\"text\",\"text\":\"still here\"}]}]}";

        Assert.True(ConversationArchiver.Read(json).IsOk(out Conversation? result, out _));

        ConversationMessage turn = Assert.Single(result!.Messages);
        Assert.Equal("still here", Assert.IsType<TextContent>(Assert.Single(turn.Content)).Text);
    }
}
