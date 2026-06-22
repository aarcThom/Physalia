// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Physalia.GH.Components;

namespace Physalia.GH.Panels;

/// <summary>
/// Modeless Eto panel for managing an <see cref="ImageGatherer"/>'s image list: a table of
/// path / editable alias / preview / remove, plus Add Image and Paste buttons. Every edit is
/// pushed back to the component, which re-solves live.
/// </summary>
public class ManageImagesDialog : Form
{
    private readonly ImageGatherer _component;
    private readonly ObservableCollection<ImageEntry> _rows;
    private readonly GridView _grid;
    private readonly GridColumn _aliasColumn;
    private readonly GridColumn _removeColumn;
    private string? _editingOldAlias;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManageImagesDialog"/> class.
    /// </summary>
    /// <param name="component">The component whose images this panel manages.</param>
    public ManageImagesDialog(ImageGatherer component)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));

        _rows = new ObservableCollection<ImageEntry>(_component.Entries);
        foreach (ImageEntry entry in _rows)
        {
            EnsurePreview(entry);
        }

        Title = "Manage Images";
        ClientSize = new Size(560, 420);
        Resizable = true;
        Minimizable = true;
        Maximizable = false;
        Owner = Rhino.UI.RhinoEtoApp.MainWindow;

        _aliasColumn = new GridColumn
        {
            HeaderText = "Alias",
            Editable = true,
            Width = 140,
            DataCell = new TextBoxCell { Binding = Binding.Property<ImageEntry, string>(r => r.Alias) },
        };

        _removeColumn = new GridColumn
        {
            HeaderText = string.Empty,
            Editable = false,
            Width = 32,
            DataCell = BuildRemoveCell(),
        };

        _grid = new GridView
        {
            DataStore = _rows,
            RowHeight = 46,
            ShowHeader = true,
            GridLines = GridLines.Horizontal,
        };

        _grid.Columns.Add(new GridColumn
        {
            HeaderText = "Image Path",
            Editable = false,
            Width = 230,
            DataCell = new TextBoxCell { Binding = Binding.Delegate<ImageEntry, string>(DisplayPath) },
        });
        _grid.Columns.Add(_aliasColumn);
        _grid.Columns.Add(new GridColumn
        {
            HeaderText = "Preview",
            Editable = false,
            Width = 60,
            DataCell = new ImageViewCell { Binding = Binding.Property<ImageEntry, Image>(r => r.Preview!) },
        });
        _grid.Columns.Add(_removeColumn);

        _grid.CellEditing += OnCellEditing;
        _grid.CellEdited += OnCellEdited;
        _grid.CellClick += OnCellClick;

        var addButton = new Button { Text = "Add Image" };
        addButton.Click += (_, _) => OnAddImage();

        var pasteButton = new Button { Text = "Paste" };
        pasteButton.Click += (_, _) => OnPaste();

        var buttons = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Items = { addButton, pasteButton },
        };

        Content = new TableLayout
        {
            Padding = 10,
            Spacing = new Size(6, 6),
            Rows =
            {
                new TableRow(new TableCell(_grid, true)) { ScaleHeight = true },
                new TableRow(buttons),
            },
        };
    }

    private static string DisplayPath(ImageEntry entry)
    {
        if (string.IsNullOrEmpty(entry.FilePath))
        {
            return "(clipboard)";
        }

        return entry.FilePath!.Length > 48 ? "…" + entry.FilePath![^47..] : entry.FilePath!;
    }

    private static void EnsurePreview(ImageEntry entry)
    {
        if (entry.Preview is not null || entry.Data.Length == 0)
        {
            return;
        }

        try
        {
            using var source = new Bitmap(entry.Data);
            const int max = 40;
            double scale = Math.Min((double)max / source.Width, (double)max / source.Height);
            int width = Math.Max(1, (int)(source.Width * scale));
            int height = Math.Max(1, (int)(source.Height * scale));

            var thumb = new Bitmap(width, height, PixelFormat.Format32bppRgba);
            using (var graphics = new Graphics(thumb))
            {
                graphics.ImageInterpolation = ImageInterpolation.High;
                graphics.DrawImage(source, 0, 0, width, height);
            }

            entry.Preview = thumb;
        }
        catch
        {
            entry.Preview = null;
        }
    }

    private static DrawableCell BuildRemoveCell()
    {
        var cell = new DrawableCell();
        cell.Paint += (_, e) =>
        {
            RectangleF bounds = e.ClipRectangle;
            var font = SystemFonts.Default();
            SizeF size = e.Graphics.MeasureString(font, "✕");
            float x = bounds.X + ((bounds.Width - size.Width) / 2f);
            float y = bounds.Y + ((bounds.Height - size.Height) / 2f);
            e.Graphics.DrawText(font, Colors.Red, x, y, "✕");
        };
        return cell;
    }

    private void OnCellEditing(object? sender, GridViewCellEventArgs e)
    {
        _editingOldAlias = (e.Item as ImageEntry)?.Alias;
    }

    private void OnCellEdited(object? sender, GridViewCellEventArgs e)
    {
        if (e.GridColumn != _aliasColumn || e.Item is not ImageEntry row)
        {
            return;
        }

        // Defer validation, reload, and re-solve until the grid has finished committing the
        // edit. Pressing Enter commits the cell mid-event; reloading or expiring the solution
        // synchronously here re-enters the grid and crashes.
        string? previous = _editingOldAlias;
        Application.Instance.AsyncInvoke(() => ApplyAliasEdit(row, previous));
    }

    private void ApplyAliasEdit(ImageEntry row, string? previous)
    {
        if (!_rows.Contains(row))
        {
            return;
        }

        string candidate = row.Alias?.Trim() ?? string.Empty;
        bool blank = string.IsNullOrEmpty(candidate);
        bool hasWhitespace = candidate.Any(char.IsWhiteSpace);
        bool duplicate = _rows.Any(r =>
            !ReferenceEquals(r, row) && string.Equals(r.Alias, candidate, StringComparison.OrdinalIgnoreCase));

        if (blank || hasWhitespace || duplicate)
        {
            // Revert via the model; the property-change notification refreshes the cell
            // without a collection Refresh (forbidden by WPF during an edit transaction).
            row.Alias = previous ?? string.Empty;
            string message = blank ? "Alias cannot be blank."
                : hasWhitespace ? "Alias cannot contain spaces — it is referenced in a prompt as \"/<alias>\"."
                : $"Alias \"{candidate}\" is already in use.";
            MessageBox.Show(
                this,
                message,
                "Invalid alias",
                MessageBoxButtons.OK,
                MessageBoxType.Warning);
            return;
        }

        row.Alias = candidate;
        _component.ReplaceEntries(_rows);
    }

    private void OnCellClick(object? sender, GridCellMouseEventArgs e)
    {
        if (e.GridColumn != _removeColumn || e.Item is not ImageEntry row)
        {
            return;
        }

        _rows.Remove(row);
        _component.ReplaceEntries(_rows);
    }

    private void OnAddImage()
    {
        using var dialog = new OpenFileDialog { MultiSelect = true };
        dialog.Filters.Add(new FileFilter("Images", ".png", ".jpg", ".jpeg"));

        if (dialog.ShowDialog(this) != DialogResult.Ok)
        {
            return;
        }

        foreach (string path in dialog.Filenames)
        {
            string? mime = ImageGatherer.MimeFromExtension(path);
            if (mime is null)
            {
                MessageBox.Show(this, $"Unsupported image type: {path}", "Add Image", MessageBoxButtons.OK, MessageBoxType.Warning);
                continue;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not read {path}: {ex.Message}", "Add Image", MessageBoxButtons.OK, MessageBoxType.Error);
                continue;
            }

            var entry = new ImageEntry
            {
                FilePath = path,
                Alias = ImageGatherer.UniqueAlias(ImageGatherer.SanitizeAlias(Path.GetFileNameWithoutExtension(path)), _rows),
                Data = data,
                MimeType = mime,
            };
            EnsurePreview(entry);
            _rows.Add(entry);
        }

        _component.ReplaceEntries(_rows);
    }

    private void OnPaste()
    {
        Image? clipboardImage = new Clipboard().Image;
        if (clipboardImage is null)
        {
            MessageBox.Show(this, "No image on the clipboard.", "Paste", MessageBoxButtons.OK, MessageBoxType.Information);
            return;
        }

        byte[] data;
        try
        {
            Bitmap bitmap = clipboardImage as Bitmap ?? new Bitmap(clipboardImage);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            data = stream.ToArray();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not read clipboard image: {ex.Message}", "Paste", MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }

        var entry = new ImageEntry
        {
            FilePath = null,
            Alias = ImageGatherer.UniqueAlias("pasted", _rows),
            Data = data,
            MimeType = "image/png",
        };
        EnsurePreview(entry);
        _rows.Add(entry);
        _component.ReplaceEntries(_rows);
    }
}
