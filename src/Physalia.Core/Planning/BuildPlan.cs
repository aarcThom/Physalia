// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System.Collections.Generic;

namespace Physalia.Core.Planning;

/// <summary>
/// One stage of an incremental build: a self-contained slice of the definition that is placed,
/// solved, and measured on its own before the next slice is authored.
/// </summary>
/// <param name="Number">The stage number the model authored, 1-based.</param>
/// <param name="Description">What this stage builds, in the model's own words.</param>
public record BuildStage(int Number, string Description);

/// <summary>
/// The staged construction plan a model declares before it emits its first document, restated in
/// every subsequent response. Physalia never authors or edits a plan — it only reads back the one
/// the model wrote, so that the feedback a stage produces can be weighed against the stages still
/// outstanding. Without it a clean geometry report reads as "done" at stage one, which is exactly
/// how an incremental build collapses back into a single-shot generation.
/// </summary>
/// <param name="Goal">The whole request in one line — what the finished definition must do.</param>
/// <param name="Stages">The ordered stages, as authored.</param>
/// <param name="CurrentStage">
/// The stage this response builds, or 0 when the response did not say. A caller that remembers the
/// previous stage should treat 0 as "unchanged" rather than advancing: a correction round rebuilds
/// the same stage, and guessing forward would mark an unbuilt stage as done.
/// </param>
public record BuildPlan(string Goal, IReadOnlyList<BuildStage> Stages, int CurrentStage);
