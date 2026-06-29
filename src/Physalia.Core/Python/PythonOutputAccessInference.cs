// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Physalia.Core.Python;

/// <summary>
/// Infers, from the source of a GH Python Script component, which of its declared variables are
/// assigned a Python <b>list</b>. A model that emits such a variable as an <c>item</c>-access output
/// produces a component that wraps the whole list as one opaque object on the canvas (unreadable by
/// downstream geometry components); promoting that output to <c>list</c> access fixes it. This is a
/// deterministic safety net so a model slip on the declared access cannot produce a broken component.
///
/// <para>Detection is a conservative static heuristic. It recognises the direct assignment shapes that
/// real generated code uses (a list literal or comprehension, <c>list(...)</c>, an augmented
/// <c>+=</c> with a literal, or <c>append</c>/<c>extend</c>/<c>insert</c> mutation), and it follows
/// one further step that generated code reaches for constantly: a <b>simple alias</b>, where an output
/// is assigned a bare variable that is itself a list (e.g. building <c>curves</c> with a loop, then
/// <c>face = curves</c>). Alias chains are resolved transitively. It does not parse Python, so it still
/// cannot follow lists produced indirectly (a function return, an indexing expression, a conditional);
/// a miss simply leaves the model's declared access untouched, and a false positive at worst yields a
/// harmless one-item list.</para>
/// </summary>
public static class PythonOutputAccessInference
{
    // name = [...]  /  name += [...]   — a list literal or comprehension at statement start.
    private static readonly Regex ListLiteralAssign = new(
        @"(?m)^[ \t]*([A-Za-z_]\w*)[ \t]*(?:=|\+=)[ \t]*\[",
        RegexOptions.Compiled);

    // name = list(...)   — explicit list construction.
    private static readonly Regex ListCtorAssign = new(
        @"(?m)^[ \t]*([A-Za-z_]\w*)[ \t]*=[ \t]*list[ \t]*\(",
        RegexOptions.Compiled);

    // name.append(...) / name.extend(...) / name.insert(...) — list mutation. The lookbehind keeps
    // this from matching an attribute or a longer identifier that ends in the captured name.
    private static readonly Regex ListMutation = new(
        @"(?<![.\w])([A-Za-z_]\w*)[ \t]*\.[ \t]*(?:append|extend|insert)[ \t]*\(",
        RegexOptions.Compiled);

    // name = other   — a simple alias: the whole right-hand side is a single bare identifier
    // (optionally followed by a trailing comment). Anything more (a call, an index, an operator)
    // is not a simple alias and is deliberately not matched.
    private static readonly Regex SimpleAlias = new(
        @"(?m)^[ \t]*([A-Za-z_]\w*)[ \t]*=[ \t]*([A-Za-z_]\w*)[ \t]*(?:#.*)?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns the subset of <paramref name="variableNames"/> that the code assigns a Python list,
    /// directly or through a simple alias chain.
    /// </summary>
    /// <param name="code">The Python source of the component.</param>
    /// <param name="variableNames">The declared variable names to test (typically output names).</param>
    /// <returns>The names assigned a list value, as an ordinal-comparison set.</returns>
    public static IReadOnlyCollection<string> InferListVariables(string code, IEnumerable<string> variableNames)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(code) || variableNames is null)
        {
            return result;
        }

        HashSet<string> directListVars = CollectDirectListVars(code);
        Dictionary<string, string> aliases = CollectSimpleAliases(code);

        foreach (string name in variableNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && ResolvesToList(name, directListVars, aliases))
            {
                result.Add(name);
            }
        }

        return result;
    }

    /// <summary>
    /// Collects every variable the code assigns a Python list through a directly recognisable shape.
    /// </summary>
    /// <param name="code">The Python source.</param>
    /// <returns>The set of directly-detected list variable names.</returns>
    private static HashSet<string> CollectDirectListVars(string code)
    {
        var vars = new HashSet<string>(StringComparer.Ordinal);
        AddCaptures(ListLiteralAssign, code, vars);
        AddCaptures(ListCtorAssign, code, vars);
        AddCaptures(ListMutation, code, vars);
        return vars;
    }

    /// <summary>
    /// Collects simple alias assignments (<c>name = other</c>) mapping each left-hand name to the
    /// bare identifier it is assigned. A name reassigned more than once keeps its last alias.
    /// </summary>
    /// <param name="code">The Python source.</param>
    /// <returns>A map from aliased name to its source identifier.</returns>
    private static Dictionary<string, string> CollectSimpleAliases(string code)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in SimpleAlias.Matches(code))
        {
            aliases[m.Groups[1].Value] = m.Groups[2].Value;
        }

        return aliases;
    }

    /// <summary>
    /// Determines whether <paramref name="name"/> is a list directly, or transitively through a chain
    /// of simple aliases that terminates at a directly-detected list variable.
    /// </summary>
    /// <param name="name">The variable name to resolve.</param>
    /// <param name="directListVars">Variables directly detected as lists.</param>
    /// <param name="aliases">Simple alias map.</param>
    /// <returns>true if the name resolves to a list.</returns>
    private static bool ResolvesToList(string name, HashSet<string> directListVars, Dictionary<string, string> aliases)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string current = name;
        while (seen.Add(current))
        {
            if (directListVars.Contains(current))
            {
                return true;
            }

            if (!aliases.TryGetValue(current, out string? next))
            {
                return false;
            }

            current = next;
        }

        return false;
    }

    private static void AddCaptures(Regex regex, string code, HashSet<string> into)
    {
        foreach (Match m in regex.Matches(code))
        {
            into.Add(m.Groups[1].Value);
        }
    }
}
