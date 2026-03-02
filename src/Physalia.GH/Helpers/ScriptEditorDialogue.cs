using Eto.Drawing;
using Eto.Forms;
using Physalia.GH.Helpers;
using System.Collections.Generic;

public class ScriptEditorDialog : Dialog<string?>
{
    private readonly TextArea _editor;
    private readonly ListBox _problemsList;
    private readonly Label _statusLabel;
    private readonly List<string> _inputNames;

    /// <summary>
    /// A simple code editor dialog for viewing and editing the generated Python script.
    /// Includes a problems panel that displays pyflakes lint diagnostics.
    /// Returns the edited script on Save, or null on Cancel.
    /// </summary>
    /// <param name="script">The current Python script to display.</param>
    /// <param name="inputNames">Variable names injected as inputs at runtime, excluded from lint.</param>
    public ScriptEditorDialog(string script, List<string> inputNames = null)
    {
        Title = "Physalia Script Editor";
        MinimumSize = new Size(700, 600);
        Padding = 10;
        Resizable = true;

        _inputNames = inputNames ?? new List<string>();

        _editor = new TextArea
        {
            Text = script,
            Font = new Font("Consolas", 10),
            SpellCheck = false,
            Wrap = false
        };

        _statusLabel = new Label
        {
            Text = "Problems",
            Font = new Font(SystemFont.Bold, 9)
        };

        _problemsList = new ListBox
        {
            Height = 120
        };

        var problemsPanel = new StackLayout
        {
            Spacing = 4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                _statusLabel,
                _problemsList
            }
        };

        var checkButton = new Button { Text = "Check" };
        checkButton.Click += (s, e) => RunLint();

        var saveButton = new Button { Text = "Save & Run" };
        saveButton.Click += (s, e) =>
        {
            Result = _editor.Text;
            Close();
        };

        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Click += (s, e) =>
        {
            Result = null;
            Close();
        };

        var buttonRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Items = { checkButton, null, saveButton, cancelButton }
        };

        Content = new StackLayout
        {
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(_editor, expand: true),
                problemsPanel,
                buttonRow
            }
        };

        DefaultButton = saveButton;
        AbortButton = cancelButton;

        RunLint();
    }

    private void RunLint()
    {
        _problemsList.Items.Clear();

        var diagnostics = CodeChecker.Check(_editor.Text, _inputNames);

        if (diagnostics.Count == 0)
        {
            _statusLabel.Text = "Problems — No issues found ✓";
            _problemsList.Items.Add(new ListItem { Text = "No issues found." });
        }
        else
        {
            _statusLabel.Text = $"Problems — {diagnostics.Count} issue(s)";
            foreach (var d in diagnostics)
            {
                _problemsList.Items.Add(new ListItem { Text = d.ToString() });
            }
        }
    }
}