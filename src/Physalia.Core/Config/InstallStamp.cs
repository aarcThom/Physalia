// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Physalia.Core.Config;

/// <summary>
/// What version of Physalia ran here last, so a SILENT package update can be reported to the user
/// the next time they open the chat window.
/// </summary>
/// <remarks>
/// <para><b>Why anything has to be recorded at all.</b> Rhino 8 updates package-manager plug-ins by
/// itself, at startup, with no prompt and no notification. That is a reasonable default and a bad
/// surprise: the user's pipeline can behave differently from how it behaved yesterday, for reasons
/// they never agreed to and cannot see. Comparing the running version to the last one recorded here
/// is the only way the plug-in can know that happened.</para>
/// <para><b>Plain JSON, not the encrypted store.</b> It is not a secret, and somebody diagnosing an
/// update should be able to read it — and delete it — with a text editor.</para>
/// <para>Four cases, and only one of them is news:</para>
/// <list type="bullet">
/// <item><description><b>No stamp</b> — a first install. Recorded, nothing shown: opening a
/// "you have been updated" dialog at somebody's first launch is simply a lie.</description></item>
/// <item><description><b>A lower version</b> — an update happened. This is the one that
/// notifies.</description></item>
/// <item><description><b>A higher version</b> — a downgrade, or Rhino 7 and 8 sharing the data
/// folder side by side. Recorded, nothing shown; there is nothing to warn about, and crying update
/// on every switch between two Rhinos would be worse than silence.</description></item>
/// <item><description><b>The same version</b> — an ordinary restart.</description></item>
/// </list>
/// <para><b>The notice is cleared by acknowledgement, not by delivery.</b> Rhino often starts with
/// no chat window open, so a stamp written the moment the notice was computed would swallow the one
/// notice the user was owed. See <c>PhyStartup.AcknowledgeNotice</c>.</para>
/// </remarks>
/// <param name="Version">
/// The full four-part version that last ran here, or null when nothing has.
/// </param>
/// <param name="FirstSeen">When Physalia first ran on this machine, as far as this file knows.</param>
/// <param name="AcknowledgedVersion">
/// The version whose update notice the user has already seen, so it is not shown twice.
/// </param>
/// <param name="NotifyOnUpdate">
/// False once the user has asked not to be told again. The stamp is still kept up to date — they
/// asked to stop being interrupted, not to stop being tracked.
/// </param>
public sealed record InstallStamp(
    string? Version = null,
    DateTimeOffset? FirstSeen = null,
    string? AcknowledgedVersion = null,
    bool NotifyOnUpdate = true)
{
    /// <summary>
    /// The file name, in the user's data folder.
    /// </summary>
    public const string FileName = "install.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads the stamp from a data folder.
    /// </summary>
    /// <param name="dataRoot">The user's data folder (<see cref="PhyData.Root"/>).</param>
    /// <returns>
    /// The stamp, or an empty one when the file is missing, unreadable or malformed. A corrupt stamp
    /// is treated as a first install: losing the record costs one un-shown notice, while throwing
    /// here would take the plug-in down with it.
    /// </returns>
    public static InstallStamp Load(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            return new InstallStamp();
        }

        string path = Path.Combine(dataRoot, FileName);

        try
        {
            if (!File.Exists(path))
            {
                return new InstallStamp();
            }

            return JsonSerializer.Deserialize<InstallStamp>(File.ReadAllText(path), ReadOptions)
                ?? new InstallStamp();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new InstallStamp();
        }
    }

    /// <summary>
    /// Writes the stamp to a data folder.
    /// </summary>
    /// <param name="dataRoot">The user's data folder (<see cref="PhyData.Root"/>).</param>
    /// <returns>True when it was written.</returns>
    public bool Save(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(dataRoot);
            File.WriteAllText(Path.Combine(dataRoot, FileName), JsonSerializer.Serialize(this, WriteOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Works out what, if anything, to tell the user about the version now running.
    /// </summary>
    /// <param name="running">The running build.</param>
    /// <returns>The notice, or null when this run is not news.</returns>
    public UpdateNotice? NoticeFor(PhyVersionInfo running)
    {
        ArgumentNullException.ThrowIfNull(running);

        if (!NotifyOnUpdate || Version is null)
        {
            return null;
        }

        if (!TryParse(Version, out Version? previous) || !TryParse(running.Full, out Version? current))
        {
            return null;
        }

        // Only forwards. A downgrade, or two Rhino versions sharing this folder, is not an update.
        if (previous >= current)
        {
            return null;
        }

        if (AcknowledgedVersion is not null
            && TryParse(AcknowledgedVersion, out Version? acknowledged)
            && acknowledged >= current)
        {
            return null;
        }

        return new UpdateNotice(PhyVersion.Display(previous), running.Display);
    }

    /// <summary>
    /// The stamp as it should be recorded for the running build.
    /// </summary>
    /// <param name="running">The running build.</param>
    /// <returns>The updated stamp, unchanged when there is nothing to record.</returns>
    public InstallStamp WithVersion(PhyVersionInfo running)
    {
        ArgumentNullException.ThrowIfNull(running);

        return this with
        {
            Version = running.Full,
            FirstSeen = FirstSeen ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// The stamp with an update notice marked as seen.
    /// </summary>
    /// <param name="version">The full version whose notice was shown.</param>
    /// <param name="notifyAgain">False when the user asked not to be told about future updates.</param>
    /// <returns>The updated stamp.</returns>
    public InstallStamp Acknowledge(string version, bool notifyAgain = true) =>
        this with { AcknowledgedVersion = version, NotifyOnUpdate = notifyAgain };

    // Version.TryParse rejects a bare "1", which is a legal <Version> in a csproj.
    private static bool TryParse(string? value, out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return System.Version.TryParse(value.Contains('.') ? value : value + ".0", out version);
    }
}

/// <summary>
/// What the chat window says when a package update has happened behind the user's back.
/// </summary>
/// <param name="From">The version that ran here last, trimmed for reading.</param>
/// <param name="To">The version running now, trimmed for reading.</param>
/// <param name="Notes">
/// This release's section of the shipped changelog, when there is one. Optional on purpose: two
/// version numbers say THAT something changed and nothing about what, but a missing changelog must
/// not cost the user the notice itself.
/// </param>
public sealed record UpdateNotice(string From, string To, string? Notes = null);
