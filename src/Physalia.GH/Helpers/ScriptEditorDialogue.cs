using Eto.Drawing;
using Eto.Forms;
using Physalia.GH.Helpers;
using System;
using System.Collections.Generic;

public class ScriptEditorDialog : Dialog<string?>
{
    private readonly TextArea _editor; // the actual editor area
    private readonly Drawable _gutter; // drawable instead of TextArea so that we can color red upon errors
    private readonly ListBox _problemsList;
    private readonly Label _statusLabel;
    private readonly List<string> _inputNames;

    private readonly Font _editorFont; // Store the font so the canvas can use it
    private readonly float _dynamicLineHeight; // the line height used to sync gutter and editor
    private HashSet<int> _errorLines = new HashSet<int>(); // Tracks broken lines

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


        // SETTING UP THE FONT ========================================================
        _editorFont = new Font(FontFamilies.Monospace, 10);

        // Measuring the font size to set the line height for the editor
        // Create a  1x1 off-screen bitmap to borrow its Graphics context
        using (var bmp = new Bitmap(1, 1, PixelFormat.Format32bppRgba))
        using (var g = new Graphics(bmp))
        {
            // Measure a string with tall ascenders ('M') and deep descenders ('g')
            var size = g.MeasureString(_editorFont, "Mg");

            // Round up to the nearest whole pixel to ensure we don't clip the bottom
            _dynamicLineHeight = size.Height;
        }

        // COMPONENTS =========================================================

        // EDITOR----------------------------------
        _editor = new TextArea
        {
            Text = script,
            Font = _editorFont,
            SpellCheck = false,
            Wrap = false,
            // TO DO - REMOVE BORDER ... maybe
        };

        // GUTTER ------------------------------------
        _gutter = new Drawable
        {
            Width = 32,
            BackgroundColor = Colors.WhiteSmoke
            
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
                _gutter,
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
        _editor.TextChanged += (s, e) => RunLint();
        _editor.TextChanged += (s, e) => SyncEditorHeightAndGutter();
        _gutter.Paint += PaintGutterCanvas;

        // RUN THESE ONCE AT THE OUTSET TO POPULATE GUTTER AND LINT
        RunLint();
        SyncEditorHeightAndGutter(); // Run once on startup
    }

    private void PaintGutterCanvas(object sender, PaintEventArgs e)
    {
        int lines = string.IsNullOrEmpty(_editor.Text) ? 1 : _editor.Text.Split('\n').Length;

        for (int i = 1; i <= lines; i++)
        {
            // Calculate the exact vertical pixel position of this line
            float yPos = (i - 1) * _dynamicLineHeight - 1f;

            // If this line has a pyflakes error, paint the background red!
            if (_errorLines.Contains(i))
            {
                // Draw a light red highlight box
                e.Graphics.FillRectangle(Color.FromArgb(255, 200, 200), 0, yPos, _gutter.Width, _dynamicLineHeight);

                // Draw a solid red warning bar on the far left edge
                e.Graphics.FillRectangle(Colors.Red, 0, yPos, 4, _dynamicLineHeight);
            }

            // Draw the actual line number
            // X=8 gives it a nice little padding from the left edge
            e.Graphics.DrawText(_editorFont, Colors.Gray, new PointF(8, yPos), i.ToString());
        }
    }

    private void SyncEditorHeightAndGutter()
    {
        int lines = string.IsNullOrEmpty(_editor.Text) ? 1 : _editor.Text.Split('\n').Length;

        int requiredHeight = (lines + 2) * (int)_dynamicLineHeight;

        _editor.Height = requiredHeight;
        _gutter.Height = requiredHeight;

        // Tell the canvas "Your data changed, please redraw the numbers!"
        _gutter.Invalidate();
    }

    /// <summary>
    /// Runs the Linter
    /// </summary>
    private void RunLint()
    {
        _problemsList.Items.Clear();
        _errorLines.Clear(); // Reset the errors

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

                // ADD THE LINE NUMBER TO OUR TRACKER
                // Note: You may need to change 'd.Line' depending on how 
                // you defined your pyflakes diagnostic object in CodeChecker!
                _errorLines.Add(d.Line);
            }
        }

        // Force the gutter to repaint so the red highlights show up
        _gutter.Invalidate();
    }
}