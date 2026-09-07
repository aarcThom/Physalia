// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System.Globalization;
using System.Text;

namespace Physalia.Core.Budget;

/// <summary>
/// What a pipeline is allowed to spend. Zero means no limit on that axis, which is how a user says
/// "cap the tokens, I do not care how many calls it takes" without a second switch to mean it.
/// </summary>
/// <param name="MaxTokens">Total tokens — prompt and completion together — or 0 for no limit.</param>
/// <param name="MaxCalls">Number of inference calls, or 0 for no limit.</param>
public sealed record SpendLimits(long MaxTokens, long MaxCalls)
{
    /// <summary>Gets limits that allow everything.</summary>
    public static SpendLimits Unlimited { get; } = new(0, 0);

    /// <summary>Gets a value indicating whether anything at all is being limited.</summary>
    public bool IsUnlimited => MaxTokens <= 0 && MaxCalls <= 0;
}

/// <summary>
/// What a pipeline has spent so far.
/// </summary>
/// <param name="Tokens">Total tokens billed, prompt and completion together.</param>
/// <param name="Calls">How many inference calls have been made.</param>
public sealed record Spend(long Tokens, long Calls)
{
    /// <summary>Gets a spend of nothing.</summary>
    public static Spend Nothing { get; } = new(0, 0);

    /// <summary>
    /// Adds one call's usage.
    /// </summary>
    /// <param name="tokens">Tokens that call cost.</param>
    /// <returns>The new total.</returns>
    public Spend Plus(long tokens) => new(Tokens + System.Math.Max(0, tokens), Calls + 1);
}

/// <summary>
/// Whether a pipeline may make another call.
/// </summary>
/// <param name="Allowed">True when the call may go ahead.</param>
/// <param name="Reason">Why not, when it may not. Null when allowed.</param>
public sealed record SpendVerdict(bool Allowed, string? Reason)
{
    /// <summary>Gets the verdict for a call that may proceed.</summary>
    public static SpendVerdict Ok { get; } = new(true, null);
}

/// <summary>
/// Decides whether a pipeline has spent its budget, and describes what it has spent.
///
/// <para><b>Checked BEFORE a call, against what is already spent.</b> Not during, and not against a
/// prediction: the cost of a call is not known until it has been made, so the only honest question is
/// whether the budget is already gone. A pipeline can therefore overrun its cap by up to one call,
/// which is the correct trade — the alternative is estimating the next call's cost and refusing on
/// the estimate, which would either block calls that would have fitted or, worse, feel arbitrary.
/// The cap's job is to stop a runaway loop, and a loop is stopped just as dead one call late.</para>
///
/// <para>Pure, and separated from the counting for the reason every policy in this project is: a dev
/// box with a real conversation on it is not a test fixture, and the interesting cases here — exactly
/// at the limit, one axis capped and the other not, a limit lowered below what is already spent — are
/// all arithmetic.</para>
/// </summary>
public static class SpendPolicy
{
    /// <summary>
    /// Whether another inference call is within budget.
    /// </summary>
    /// <param name="spent">What has been spent so far.</param>
    /// <param name="limits">What is allowed.</param>
    /// <returns>The verdict, carrying the reason when it refuses.</returns>
    public static SpendVerdict Check(Spend? spent, SpendLimits? limits)
    {
        Spend actual = spent ?? Spend.Nothing;
        SpendLimits caps = limits ?? SpendLimits.Unlimited;

        if (caps.MaxCalls > 0 && actual.Calls >= caps.MaxCalls)
        {
            return new SpendVerdict(
                false,
                $"This pipeline's call budget is spent: {Count(actual.Calls)} of {Count(caps.MaxCalls)} allowed.");
        }

        if (caps.MaxTokens > 0 && actual.Tokens >= caps.MaxTokens)
        {
            return new SpendVerdict(
                false,
                $"This pipeline's token budget is spent: {Tokens(actual.Tokens)} of {Tokens(caps.MaxTokens)} allowed.");
        }

        return SpendVerdict.Ok;
    }

    /// <summary>
    /// Describes what has been spent against what is allowed, for a caption or a card.
    /// </summary>
    /// <param name="spent">What has been spent.</param>
    /// <param name="limits">What is allowed.</param>
    /// <returns>A single line, e.g. "182k / 200k tokens · 14 / 50 calls".</returns>
    public static string Describe(Spend? spent, SpendLimits? limits)
    {
        Spend actual = spent ?? Spend.Nothing;
        SpendLimits caps = limits ?? SpendLimits.Unlimited;

        var parts = new StringBuilder();

        parts.Append(caps.MaxTokens > 0
            ? $"{Tokens(actual.Tokens)} / {Tokens(caps.MaxTokens)} tokens"
            : $"{Tokens(actual.Tokens)} tokens");

        parts.Append(" · ");

        parts.Append(caps.MaxCalls > 0
            ? $"{actual.Calls.ToString(CultureInfo.InvariantCulture)} / {caps.MaxCalls.ToString(CultureInfo.InvariantCulture)} calls"
            : $"{Count(actual.Calls)}");

        return parts.ToString();
    }

    /// <summary>
    /// Formats a token count compactly — "182k" rather than "182,431". The exact figure is noise at
    /// the point of a decision, and a caption has a few characters to work with.
    /// </summary>
    /// <param name="tokens">The count.</param>
    /// <returns>The formatted count.</returns>
    public static string Tokens(long tokens) =>
        tokens >= 1_000_000
            ? (tokens / 1_000_000.0).ToString("0.##", CultureInfo.InvariantCulture) + "M"
            : tokens >= 1_000
                ? (tokens / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k"
                : tokens.ToString(CultureInfo.InvariantCulture);

    private static string Count(long calls) =>
        calls == 1
            ? "1 call"
            : $"{calls.ToString(CultureInfo.InvariantCulture)} calls";
}
