using Eto.Drawing;
using Eto.Forms;
using Physalia.GH.Helpers;
using System.Collections.Generic;

public class ScriptEditorDialog : Dialog<string?>
{
    private readonly TextArea _editor; // the actual editor area
    private readonly TextArea _lineNumbers; // the fake gutter for lines
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

        // The Line Height constant (Consolas 10pt is roughly 18 pixels high depending on the OS)
        // You may need to tweak this integer slightly (16, 18, 20) to match your exact OS rendering.
        int estimatedLineHeight = 18;

        _inputNames = inputNames ?? new List<string>();


        // COMPONENTS =========================================================

        // EDITOR----------------------------------
        _editor = new TextArea
        {
            Text = script,
            Font = new Font("Consolas", 10),
            SpellCheck = false,
            Wrap = false,
            // TO DO - REMOVE BORDER ... maybe
        };

        // GUTTER ------------------------------------
        _lineNumbers = new TextArea
        {
            ReadOnly = true,
            Font = new Font("Consolas", 10),
            Wrap = false,
            BackgroundColor = Colors.WhiteSmoke,
            TextColor = Colors.Gray
        };

        // hacky way to force the width of the gutter. Setting it above in _lineNumbers wasn't working....
        var gutterContainer = new Panel
        {
            Width = 32,
            Content = _lineNumbers
        };


        // PROBLEMS LABEL ----------------------------
        _statusLabel = new Label
        {
            Text = "Problems",
            Font = new Font(SystemFont.Bold, 9)
        };

        // PROBLEMS LIST -----------------------------
        _problemsList = new ListBox
        {
            Height = 120
        };


        // BUTTONS -----------------------------------

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


        // LAYOUT ===============================================================

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

        var buttonRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Items = { checkButton, null, saveButton, cancelButton }
        };

        // Create a horizontal row for the text areas
        // this won't be scrollable
        var editorRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Items =
            {
                gutterContainer,
                new StackLayoutItem(_editor, expand: true)
            }
        };

        // The scrollable container for both the editor and gutter
        var scrollContainer = new Scrollable
        {
            Content = editorRow,
            Border = BorderType.Line,
            ExpandContentWidth = true,
            Height = 200,
            // False means the content dictates the height, forcing the scrollbar to appear
            ExpandContentHeight = false
        };

        // MAIN LAYOUT ----------------------------------------------------
        Content = new StackLayout
        {
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(scrollContainer, expand: true),
                problemsPanel,
                buttonRow
            }
        };

        DefaultButton = saveButton;
        AbortButton = cancelButton;


        // EVENTS AND FORMATTING
        // Update the line numbers whenever the text changes 
        _editor.TextChanged += (s, e) => UpdateLineNumbers();
        _editor.TextChanged += (s, e) => RunLint();
        _editor.TextChanged += (s, e) => SyncEditorHeightAndGutter();

        // RUN THESE ONCE AT THE OUTSET TO POPULATE GUTTER AND LINT
        RunLint();
        UpdateLineNumbers(); // Call it once to populate the initial numbers
        SyncEditorHeightAndGutter(); // Run once on startup
    }

    /// <summary>
    /// Updates the numbers in the gutter.
    /// TODO: THIS DOESN'T WORK WITH SCROLL. NEED TO FIGURE THIS OUT.
    /// </summary>
    private void UpdateLineNumbers()
    {
        // Count how many lines exist in the main editor
        int lines = string.IsNullOrEmpty(_editor.Text) ? 1 : _editor.Text.Split('\n').Length;

        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= lines; i++)
        {
            sb.Append(i).Append("\n");
        }

        _lineNumbers.Text = sb.ToString();
    }

    private void SyncEditorHeightAndGutter()
    {
        // 1. Calculate how many lines exist
        int lines = string.IsNullOrEmpty(_editor.Text) ? 1 : _editor.Text.Split('\n').Length;

        // 2. Generate the gutter text
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= lines; i++)
        {
            sb.Append(i).Append("\n");
        }
        _lineNumbers.Text = sb.ToString();

        // Calculate total required physical height
        // Add a few extra lines of padding at the bottom so it doesn't clip
        int requiredHeight = (lines + 2) * 18; // 18 is our estimated line height

        // Force both controls to physically grow
        // We set the size so the Scrollable container knows exactly how big its children are
        _editor.Size = new Size(-1, requiredHeight);
        _lineNumbers.Size = new Size(-1, requiredHeight);
    }

    /// <summary>
    /// Runs the Linter
    /// </summary>
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