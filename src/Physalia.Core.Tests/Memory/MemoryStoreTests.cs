// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using Physalia.Core.Memory;
using Xunit;

namespace Physalia.Core.Tests.Memory;

public sealed class MemoryStoreTests : IDisposable
{
    private readonly string _base;
    private readonly MemoryRoots _roots;

    public MemoryStoreTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "physalia-mem-tests", Guid.NewGuid().ToString("N"));
        _roots = new MemoryRoots(
            Path.Combine(_base, "global"),
            Path.Combine(_base, "local", "doc"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_base))
            {
                Directory.Delete(_base, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private MemoryOutcome Exec(string json) => MemoryStore.Execute(json, _roots);

    [Fact]
    public void Create_WritesFileUnderGlobal()
    {
        MemoryOutcome outcome = Exec("{\"command\":\"create\",\"path\":\"/memories/global/notes.md\",\"file_text\":\"hello\"}");

        Assert.False(outcome.IsError);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_roots.GlobalDir, "notes.md")));
    }

    [Fact]
    public void Create_WritesFileUnderLocal()
    {
        MemoryOutcome outcome = Exec("{\"command\":\"create\",\"path\":\"/memories/local/todo.md\",\"file_text\":\"a\\nb\"}");

        Assert.False(outcome.IsError);
        Assert.True(File.Exists(Path.Combine(_roots.LocalDir, "todo.md")));
    }

    [Fact]
    public void View_FileReturnsNumberedContents()
    {
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/a.md\",\"file_text\":\"one\\ntwo\"}");

        MemoryOutcome outcome = Exec("{\"command\":\"view\",\"path\":\"/memories/global/a.md\"}");

        Assert.False(outcome.IsError);
        Assert.Contains("one", outcome.Content);
        Assert.Contains("two", outcome.Content);
    }

    [Fact]
    public void View_RootListsBothScopes()
    {
        MemoryOutcome outcome = Exec("{\"command\":\"view\",\"path\":\"/memories\"}");

        Assert.False(outcome.IsError);
        Assert.Contains("global", outcome.Content);
        Assert.Contains("local", outcome.Content);
    }

    [Fact]
    public void View_DirectoryListsFiles()
    {
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/a.md\",\"file_text\":\"x\"}");
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/b.md\",\"file_text\":\"y\"}");

        MemoryOutcome outcome = Exec("{\"command\":\"view\",\"path\":\"/memories/global\"}");

        Assert.False(outcome.IsError);
        Assert.Contains("a.md", outcome.Content);
        Assert.Contains("b.md", outcome.Content);
    }

    [Fact]
    public void StrReplace_ReplacesUniqueMatch()
    {
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/a.md\",\"file_text\":\"the quick fox\"}");

        MemoryOutcome outcome = Exec("{\"command\":\"str_replace\",\"path\":\"/memories/global/a.md\",\"old_str\":\"quick\",\"new_str\":\"slow\"}");

        Assert.False(outcome.IsError);
        Assert.Equal("the slow fox", File.ReadAllText(Path.Combine(_roots.GlobalDir, "a.md")));
    }

    [Fact]
    public void StrReplace_ErrorsWhenNotUnique()
    {
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/a.md\",\"file_text\":\"x x\"}");

        MemoryOutcome outcome = Exec("{\"command\":\"str_replace\",\"path\":\"/memories/global/a.md\",\"old_str\":\"x\",\"new_str\":\"y\"}");

        Assert.True(outcome.IsError);
    }

    [Fact]
    public void StrReplace_ErrorsWhenMissing()
    {
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/a.md\",\"file_text\":\"abc\"}");

        MemoryOutcome outcome = Exec("{\"command\":\"str_replace\",\"path\":\"/memories/global/a.md\",\"old_str\":\"zzz\",\"new_str\":\"y\"}");

        Assert.True(outcome.IsError);
    }

    [Fact]
    public void Insert_AtTopAndBody()
    {
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/a.md\",\"file_text\":\"line1\\nline2\"}");

        MemoryOutcome outcome = Exec("{\"command\":\"insert\",\"path\":\"/memories/global/a.md\",\"insert_line\":1,\"insert_text\":\"middle\"}");

        Assert.False(outcome.IsError);
        Assert.Equal("line1\nmiddle\nline2", File.ReadAllText(Path.Combine(_roots.GlobalDir, "a.md")));
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/a.md\",\"file_text\":\"x\"}");

        MemoryOutcome outcome = Exec("{\"command\":\"delete\",\"path\":\"/memories/global/a.md\"}");

        Assert.False(outcome.IsError);
        Assert.False(File.Exists(Path.Combine(_roots.GlobalDir, "a.md")));
    }

    [Fact]
    public void Rename_MovesFile()
    {
        Exec("{\"command\":\"create\",\"path\":\"/memories/global/a.md\",\"file_text\":\"x\"}");

        MemoryOutcome outcome = Exec("{\"command\":\"rename\",\"old_path\":\"/memories/global/a.md\",\"new_path\":\"/memories/global/b.md\"}");

        Assert.False(outcome.IsError);
        Assert.False(File.Exists(Path.Combine(_roots.GlobalDir, "a.md")));
        Assert.True(File.Exists(Path.Combine(_roots.GlobalDir, "b.md")));
    }

    [Fact]
    public void Path_TraversalIsRejected()
    {
        MemoryOutcome outcome = Exec("{\"command\":\"create\",\"path\":\"/memories/global/../escape.md\",\"file_text\":\"x\"}");

        Assert.True(outcome.IsError);
        Assert.False(File.Exists(Path.Combine(_base, "escape.md")));
    }

    [Fact]
    public void Path_UnknownScopeIsRejected()
    {
        MemoryOutcome outcome = Exec("{\"command\":\"create\",\"path\":\"/memories/elsewhere/x.md\",\"file_text\":\"x\"}");

        Assert.True(outcome.IsError);
    }

    [Fact]
    public void UnknownCommand_IsError()
    {
        Assert.True(Exec("{\"command\":\"frobnicate\"}").IsError);
    }

    [Fact]
    public void BadJson_IsError()
    {
        Assert.True(Exec("not json").IsError);
    }
}
