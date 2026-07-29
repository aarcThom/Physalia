// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Physalia.Core.Planning;

/// <summary>
/// Reads the staged build plan out of a raw model response and renders it back as a progress
/// digest. Both halves are pure string work: the plan is authored by the model as a plain-text
/// block ahead of its JSON document, never as a field inside the document, so nothing here can
/// affect what gets placed on the canvas — the JSON extractor strips the block on its way to the
/// validators, and this parser reads the same text on its way to the feedback.
///
/// <para>Keeping the plan outside the document is deliberate. A <c>buildPlan</c> property inside
/// the GhJSON would have to survive our schema, the GhJSON library's schema, and its
/// deserializer, and a rejection there would fail a submission for a reason that has nothing to
/// do with the graph. Prose ahead of the JSON is already a supported shape.</para>
/// </summary>
public static class BuildPlanParser
{
    /// <summary>
    /// The opening line of a rendered progress digest. The Geometry Report matches on this to
    /// recognise that its operator note carries build progress, and hands the "what to do next"
    /// instruction over to the digest instead of printing its own single-shot wording.
    /// </summary>
    public const string DigestMarker = "BUILD PROGRESS";

    private const string OpenTag = "<plan>";
    private const string CloseTag = "</plan>";

    // "1. Ground floor mass", "2) Roof", "stage 3 - windows". The separator is optional so a bare
    // "4 Window openings" still reads as a stage.
    private static readonly Regex StageLine = new(
        @"^(?:stage\s*)?(\d{1,3})\s*[.):\-]?\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GoalLine = new(
        @"^goal\s*:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NowLine = new(
        @"^(?:now|current(?:\s+stage)?)\s*:\s*(?:stage\s*)?(\d{1,3})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses the <c>&lt;plan&gt;…&lt;/plan&gt;</c> block out of a raw model response.
    /// Tolerant by design: markdown decoration is stripped, the closing tag may be missing
    /// (the block then runs to the start of the JSON document), and any line that matches
    /// nothing is ignored. A response with no block, or a block that lists no stages, yields
    /// null — the pipeline then behaves exactly as it does without incremental building.
    /// </summary>
    /// <param name="response">The raw response text, plan block and JSON document included.</param>
    /// <returns>The declared plan, or null when the response declares none.</returns>
    public static BuildPlan? Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        int open = response!.IndexOf(OpenTag, StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return null;
        }

        open += OpenTag.Length;
        int close = response.IndexOf(CloseTag, open, StringComparison.OrdinalIgnoreCase);
        string body = close < 0 ? response[open..] : response[open..close];

        var stages = new List<BuildStage>();
        var seen = new HashSet<int>();
        string goal = string.Empty;
        int current = 0;

        foreach (string raw in body.Split('\n'))
        {
            string line = raw.Trim().Trim('*', '#', '>', '`').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // An unterminated block runs into the document; stop at the first line that opens it.
            if (close < 0 && (line[0] == '{' || line[0] == '['))
            {
                break;
            }

            if (GoalLine.Match(line) is { Success: true } goalMatch)
            {
                goal = Clean(goalMatch.Groups[1].Value);
                continue;
            }

            if (NowLine.Match(line) is { Success: true } nowMatch
                && int.TryParse(nowMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int now))
            {
                current = now;
                continue;
            }

            if (StageLine.Match(line) is { Success: true } stageMatch
                && int.TryParse(stageMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                && seen.Add(number))
            {
                stages.Add(new BuildStage(number, Clean(stageMatch.Groups[2].Value)));
            }
        }

        if (stages.Count == 0)
        {
            return null;
        }

        stages.Sort((a, b) => a.Number.CompareTo(b.Number));
        return new BuildPlan(goal, stages, current);
    }

    // Markdown emphasis a model wraps around a value rather than the whole line — "**goal:** a
    // tower" survives the line-level trim with the asterisks still attached to the value.
    private static string Clean(string value) => value.Trim().Trim('*', '`').Trim();

    /// <summary>
    /// Reads a bare <c>now: N</c> stage pointer from a response that declares no plan block.
    ///
    /// <para>This is the counterpart to the digest no longer asking for a restatement. Once
    /// Physalia holds the plan and prints it back every round, a restatement is pure repetition —
    /// so the model is asked for the pointer alone, and the pointer has to be readable without the
    /// block around it. A caller that already holds a plan uses this to advance its stage.</para>
    ///
    /// <para>Only the prose ahead of the document is scanned. A <c>now:</c> appearing inside the
    /// JSON — in a panel's text, say — is data the model authored for the canvas, not a pointer at
    /// Physalia.</para>
    /// </summary>
    /// <param name="response">The raw response text.</param>
    /// <returns>The declared stage number, or 0 when the response declares none.</returns>
    public static int ParseCurrentStage(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return 0;
        }

        foreach (string raw in response!.Split('\n'))
        {
            string line = raw.Trim().Trim('*', '#', '>', '`').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // The document starts here; anything past it is canvas content, not a pointer.
            if (line[0] == '{' || line[0] == '[')
            {
                break;
            }

            if (NowLine.Match(line) is { Success: true } match
                && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stage))
            {
                return stage;
            }
        }

        return 0;
    }

    /// <summary>
    /// Removes a <c>&lt;plan&gt;…&lt;/plan&gt;</c> block from a response, leaving everything else
    /// byte-identical. Used by compaction to strip the plan out of OLD assistant turns: the model
    /// restates its whole plan in every response, so an N-turn window replays N copies of it, and
    /// the only copy that carries information is the current one — which the Build Plan tracker
    /// reads back authoritatively in the progress digest anyway.
    ///
    /// <para>Only a properly closed block is removed. An unterminated block runs into the JSON
    /// document, and guessing where it ends risks eating the document with it.</para>
    /// </summary>
    /// <param name="response">The response text.</param>
    /// <returns>The response without its plan block, or unchanged when it has no closed block.</returns>
    public static string StripPlanBlock(string? response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return response ?? string.Empty;
        }

        int open = response!.IndexOf(OpenTag, StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return response;
        }

        int close = response.IndexOf(CloseTag, open, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return response;
        }

        return (response[..open] + response[(close + CloseTag.Length)..]).Trim();
    }

    /// <summary>
    /// Renders the plan as the progress digest that rides into the geometry report: the stages
    /// with their state, the count still outstanding, and the one instruction that decides
    /// whether the loop continues. The digest, not the report, owns that instruction — the
    /// report's own "reply in prose if this matches your intent" is correct for a single-shot
    /// generation and catastrophic for an incremental one, where it offers an exit at stage one.
    /// </summary>
    /// <param name="plan">The plan as declared.</param>
    /// <param name="stage">
    /// The stage just submitted (the caller's resolved value, which may differ from
    /// <see cref="BuildPlan.CurrentStage"/> when the response omitted it).
    /// </param>
    /// <returns>The digest text.</returns>
    public static string RenderProgress(BuildPlan plan, int stage)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var sb = new StringBuilder();
        sb.AppendLine(
            DigestMarker + " — you are building this definition in stages, and the build is NOT "
            + "finished until every stage below has been placed and measured. This is your own "
            + "plan, read back to you.");

        if (!string.IsNullOrWhiteSpace(plan.Goal))
        {
            sb.AppendLine("Goal: " + plan.Goal);
        }

        int remaining = 0;
        BuildStage? next = null;
        foreach (BuildStage item in plan.Stages)
        {
            string mark;
            if (item.Number < stage)
            {
                mark = "[built]";
            }
            else if (item.Number == stage)
            {
                mark = "[ NOW ]";
            }
            else
            {
                mark = "[to do]";
                remaining++;
                next ??= item;
            }

            sb.AppendLine($"  {mark} {item.Number}. {item.Description}");
        }

        sb.AppendLine(
            $"The measurements below are the result of stage {stage}"
            + (remaining > 0
                ? $"; {remaining} stage{(remaining == 1 ? string.Empty : "s")} still to build after it."
                : " — the last stage in your plan."));

        sb.AppendLine();

        // The plan itself is NOT requested back. It is printed in full above, from Physalia's own
        // parse of the model's original declaration, so a restatement would be the same text a
        // third time in one exchange — and every restatement is then replayed by the window on
        // every later turn. All that is actually needed from the model is the stage pointer.
        sb.Append(remaining > 0
            ? "WHAT TO DO NEXT: if the measurements match what stage " + stage + " was meant to "
              + "build, reply with the ghpatch that adds stage " + next!.Number + " (" + next.Description
              + ") and nothing else. If they do not match, fix stage " + stage + " with a corrective "
              + "ghpatch first and verify it before moving on. Do NOT reply in prose — the build is "
              + "unfinished, and a prose reply ends the loop with it half-built. Do NOT restate the "
              + "plan: I hold it and read it back to you above. Write a single line — 'now: "
              + next.Number + "' when advancing, 'now: " + stage + "' when correcting — ahead of the "
              + "patch, and nothing else. Amend a stage's wording only if the plan itself changed, "
              + "by writing a full plan block that turn."
            : "WHAT TO DO NEXT: this was the FINAL stage, so check the measurements against the "
              + "WHOLE goal and not just this stage. If everything the goal asks for is present and "
              + "correct, reply in plain prose confirming what was built — that ends the build. If "
              + "anything is missing or wrong, reply with a corrective ghpatch instead, led by the "
              + "single line 'now: " + stage + "' (do NOT restate the plan — I hold it).");

        return sb.ToString();
    }
}
