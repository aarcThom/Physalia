// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel.Types;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Goo;

namespace Physalia.GH.Components;

/// <summary>
/// Resolves a generic GH input (Instructions, Conversation, or string) into an
/// <see cref="Instructions"/> instance for use by token estimator components.
/// </summary>
internal static class TokenInputHelper
{
    /// <summary>
    /// Attempts to resolve a generic GH goo value into an <see cref="Instructions"/> instance.
    /// Accepts <see cref="GH_Instructions"/>, <see cref="GH_Conversation"/>, or any value
    /// whose <c>ToString()</c> produces a non-empty string (treated as a single User message).
    /// </summary>
    /// <param name="goo">The raw goo received from the generic input parameter.</param>
    /// <param name="instructions">The resolved instructions on success.</param>
    /// <returns>True if resolution succeeded; false if the input was null or empty.</returns>
    internal static bool TryResolve(IGH_Goo? goo, out Instructions instructions)
    {
        instructions = null!;

        if (goo is null) return false;

        // A signal carrying full Instructions (a Conversation Log→LLM Call event) resolves to those directly,
        // so the Token Estimator can measure a signal wire without a manual Deconstruct Signal.
        if (goo is GH_Signal sigGoo && sigGoo.Value?.Instructions is { } signalInstructions)
        {
            instructions = signalInstructions;
            return true;
        }

        if (goo is GH_Instructions instrGoo && instrGoo.Value is not null)
        {
            instructions = instrGoo.Value;
            return true;
        }

        if (goo is GH_Conversation convGoo && convGoo.Value is not null)
        {
            instructions = new Instructions(string.Empty, convGoo.Value);
            return true;
        }

        string text = goo.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return false;

        var conv = Conversation.Empty.Append(new ConversationMessage(Role.User, text));
        instructions = new Instructions(string.Empty, conv);
        return true;
    }

    /// <summary>
    /// Builds a stable string key from an Instructions instance for change-detection caching.
    /// </summary>
    /// <param name="instructions">The instructions to key.</param>
    /// <returns>A string that changes whenever the content changes.</returns>
    internal static string BuildDataKey(Instructions instructions)
    {
        return instructions.SystemPrompt + "||" + ConversationHelpers.ToDisplayString(instructions.Conversation);
    }
}
