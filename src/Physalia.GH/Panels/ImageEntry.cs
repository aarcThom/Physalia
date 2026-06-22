// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Physalia.GH.Panels;

/// <summary>
/// Mutable working record for one gathered image, shared between the
/// <see cref="Physalia.GH.Components.ImageGatherer"/> component (source of truth) and the Manage Images panel.
/// Raises <see cref="INotifyPropertyChanged"/> on <see cref="Alias"/> so the grid can
/// refresh a single cell without a full collection refresh (which WPF forbids mid-edit).
/// </summary>
public class ImageEntry : INotifyPropertyChanged
{
    private string _alias = string.Empty;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the originating file path, or null for a clipboard-pasted image.
    /// Only file-backed entries are persisted in the document.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets the user-facing alias. Unique (case-insensitive) within a component.
    /// </summary>
    public string Alias
    {
        get => _alias;
        set
        {
            if (_alias == value)
            {
                return;
            }

            _alias = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the raw image bytes.
    /// </summary>
    public byte[] Data { get; set; } = System.Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the MIME type, e.g. "image/png".
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a small preview thumbnail, built lazily by the Manage Images panel.
    /// </summary>
    public Eto.Drawing.Image? Preview { get; set; }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
