// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using Physalia.Core.Config;
using Xunit;

namespace Physalia.Core.Tests.Config;

public class InstallStampTests
{
    private static readonly PhyVersionInfo Running = PhyVersion.For(new Version(1, 2, 0, 0));

    [Fact]
    public void AFirstInstallIsNotAnUpdate()
    {
        // The stamp is missing on a first run, and a "you have been updated" dialog at somebody's
        // first launch is simply untrue.
        Assert.Null(new InstallStamp().NoticeFor(Running));
    }

    [Fact]
    public void AnOrdinaryRestartSaysNothing()
    {
        Assert.Null(new InstallStamp(Version: "1.2.0.0").NoticeFor(Running));
    }

    [Fact]
    public void AnUpdateIsReportedWithBothVersionsTrimmed()
    {
        UpdateNotice notice = Assert.IsType<UpdateNotice>(
            new InstallStamp(Version: "1.1.0.0").NoticeFor(Running));

        Assert.Equal("1.1", notice.From);
        Assert.Equal("1.2", notice.To);
        Assert.Null(notice.Notes);
    }

    [Fact]
    public void ADowngradeIsNotAnUpdate()
    {
        // Rhino 7 and Rhino 8 share this data folder, so switching between two installed versions
        // walks the stamp backwards. Crying update on every switch would be worse than silence.
        Assert.Null(new InstallStamp(Version: "1.3.0.0").NoticeFor(Running));
    }

    [Fact]
    public void AnAcknowledgedUpdateIsNotShownTwice()
    {
        InstallStamp stamp = new InstallStamp(Version: "1.1.0.0").Acknowledge(Running.Full);

        Assert.Null(stamp.NoticeFor(Running));
    }

    [Fact]
    public void AcknowledgingOneUpdateDoesNotSilenceTheNext()
    {
        InstallStamp stamp = new InstallStamp(Version: "1.1.0.0").Acknowledge("1.2.0.0");

        UpdateNotice notice = Assert.IsType<UpdateNotice>(
            stamp.NoticeFor(PhyVersion.For(new Version(1, 3, 0, 0))));

        Assert.Equal("1.3", notice.To);
    }

    [Fact]
    public void OptingOutSilencesEveryUpdate()
    {
        InstallStamp stamp = new InstallStamp(Version: "1.1.0.0").Acknowledge("1.2.0.0", notifyAgain: false);

        Assert.Null(stamp.NoticeFor(PhyVersion.For(new Version(9, 9, 0, 0))));
    }

    [Fact]
    public void RecordingTheRunningVersionKeepsTheOriginalFirstSeen()
    {
        DateTimeOffset first = DateTimeOffset.UtcNow.AddYears(-1);
        InstallStamp stamp = new InstallStamp(Version: "1.1.0.0", FirstSeen: first).WithVersion(Running);

        Assert.Equal("1.2.0.0", stamp.Version);
        Assert.Equal(first, stamp.FirstSeen);
    }

    [Fact]
    public void RecordingStampsFirstSeenWhenNothingHasRunHereBefore()
    {
        InstallStamp stamp = new InstallStamp().WithVersion(Running);

        Assert.NotNull(stamp.FirstSeen);
    }

    [Fact]
    public void RoundTripsThroughItsFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "physalia-stamp-" + Guid.NewGuid().ToString("N"));

        try
        {
            InstallStamp written = new InstallStamp().WithVersion(Running).Acknowledge(Running.Full);

            Assert.True(written.Save(dir));
            Assert.Equal(written, InstallStamp.Load(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void ACorruptStampReadsAsAFirstInstallRatherThanThrowing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "physalia-stamp-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, InstallStamp.FileName), "{ not json");

            Assert.Equal(new InstallStamp(), InstallStamp.Load(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void AMissingStampFileIsAnEmptyOne()
    {
        Assert.Equal(
            new InstallStamp(),
            InstallStamp.Load(Path.Combine(Path.GetTempPath(), "physalia-none-" + Guid.NewGuid().ToString("N"))));
    }
}
