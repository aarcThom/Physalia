// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Physalia.Core.Signals;

namespace Physalia.GH.Components;

/// <summary>
/// Routes a signal by what its payload SAYS — matching text one way, everything else the other.
///
/// <para><b>Reach for the Declare tool first.</b> If what you are testing for is the model's own
/// intention — "it has finished", "it needs the human", "it wants the next stage" — then a Declare
/// node is the right answer and this is the inferior version of it: a declared route is a decision the
/// model made deliberately, while matching for the word "done" in prose finds it in "not done yet"
/// as well. What this node IS for is text the model did not choose the shape of, and text that never
/// came from a model at all: a folder watcher's payload, a validator's complaint, a script's output,
/// a tool result carrying a status.</para>
///
/// <para>Plain contains-matching by default, because that is what nearly every use of this wants and a
/// regular expression is a thing to get wrong quietly. The context menu switches it to a regular
/// expression for the cases that need one; a pattern that will not compile says so on the node and
/// routes everything to No Match rather than pretending to test something.</para>
/// </summary>
public class SignalSwitch : SignalRelayBase
{
    private const int InPattern = 1;

    private string _pattern = string.Empty;

    private bool _regex;

    private int _matched;

    private int _unmatched;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalSwitch"/> class.
    /// </summary>
    public SignalSwitch()
        : base(
            "Signal Switch",
            "Switch",
            "Sends a signal one way if its text contains the pattern and the other way if it does not. For the model's own intentions use a Declare node instead — a declared route cannot be found inside a sentence that meant the opposite.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7B4E1C90-A268-4D53-BF07-6392E5D8A1C7");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The signals to route. Each one's payload text is tested as it arrives.";

    /// <inheritdoc/>
    protected override string PassedOutputName => "Match";

    /// <inheritdoc/>
    protected override string PassedOutputDescription =>
        "Each signal whose text matched, exactly as it arrived.";

    /// <inheritdoc/>
    protected override bool HasSecondaryOutput => true;

    /// <inheritdoc/>
    protected override string DivertedOutputName => "No Match";

    /// <inheritdoc/>
    protected override string DivertedOutputDescription =>
        "Each signal whose text did not match. This is the branch a broken pattern sends everything down, so a silent regular-expression mistake shows up here rather than nowhere.";

    /// <inheritdoc/>
    protected override string RelayCaption
    {
        get
        {
            string mode = _regex ? "regex" : "text";
            return _matched == 0 && _unmatched == 0
                ? mode
                : $"{mode} · {_matched}/{_matched + _unmatched}";
        }
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Pattern",
            "P",
            "The text to look for in the signal's payload. Case is ignored. Right-click to match it as a regular expression instead.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[InPattern].Optional = true;
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        string pattern = string.Empty;
        da.GetData(InPattern, ref pattern);
        _pattern = pattern ?? string.Empty;

        if (string.IsNullOrEmpty(_pattern))
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Remark,
                "No pattern, so nothing is being tested and every signal goes to Match.");
        }
    }

    /// <inheritdoc/>
    protected override RelayRoute Decide(PhySignal signal, bool held, IGH_DataAccess da)
    {
        if (IsMatch(signal.Payload))
        {
            _matched++;
            return RelayRoute.Pass;
        }

        _unmatched++;
        return RelayRoute.Divert;
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(
            menu,
            "Match As Regular Expression",
            (_, _) =>
            {
                _regex = !_regex;
                ExpireSolution(true);
            },
            enabled: true,
            @checked: _regex);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IO.Serialization.GH_IWriter writer)
    {
        writer.SetBoolean("Regex", _regex);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        // Absent key = a file written before the switch existed, or plain-text matching. Either way
        // plain text is the right default: reading a literal pattern as a regular expression changes
        // what a saved definition does.
        _regex = reader.ItemExists("Regex") && reader.GetBoolean("Regex");
        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _matched = 0;
        _unmatched = 0;
    }

    private bool IsMatch(string payload)
    {
        if (string.IsNullOrEmpty(_pattern))
        {
            return true;
        }

        if (!_regex)
        {
            return payload.Contains(_pattern, StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return Regex.IsMatch(payload, _pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            // A pattern that will not compile. Everything goes to No Match, and the node says why:
            // routing everything to Match would look like the test passing.
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"\"{_pattern}\" is not a valid regular expression: {ex.Message}");
            return false;
        }
    }
}
