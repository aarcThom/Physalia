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
/// <para>Detection is a conservative static heuristic — it recognises the assignment shapes that
/// real generated code uses (a list literal or comprehension, <c>list(...)</c>, an augmented
/// <c>+=</c> with a literal, or <c>append</c>/<c>extend</c>/<c>insert</c> mutation) and nothing more.
/// A miss simply leaves the model's declared access untouched; a false positive (a name only ever
/// holding a single value) at worst yields a harmless one-item list. It does not parse Python, so it
/// cannot follow data flow through intermediate variables.</para>
/// </summary>
public static class PythonOutputAccessInference
{
    /// <summary>
    /// Returns the subset of <paramref name="variableNames"/> that the code assigns a Python list.
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

        foreach (string name in variableNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && AssignsList(code, name))
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static bool AssignsList(string code, string name)
    {
        string n = Regex.Escape(name);

        // name = [...]   /   name += [...]   — a list literal or comprehension at statement start.
        if (Regex.IsMatch(code, $@"(?m)^[ \t]*{n}[ \t]*(?:=|\+=)[ \t]*\["))
        {
            return true;
        }

        // name = list(...)   — explicit list construction.
        if (Regex.IsMatch(code, $@"(?m)^[ \t]*{n}[ \t]*=[ \t]*list[ \t]*\("))
        {
            return true;
        }

        // name.append(...) / name.extend(...) / name.insert(...) — list mutation. The lookbehind
        // keeps this from matching an attribute or a longer identifier that ends in `name`.
        if (Regex.IsMatch(code, $@"(?<![.\w]){n}[ \t]*\.[ \t]*(?:append|extend|insert)[ \t]*\("))
        {
            return true;
        }

        return false;
    }
}
