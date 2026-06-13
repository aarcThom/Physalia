// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Physalia.Core.ConvoInstruct;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for an <see cref="ImageResource"/> — an image source plus
/// its user-entered alias.
///
/// <para>The wrapped value carries both the raw image bytes (as an
/// <see cref="InlineImage"/>) and the alias as first-class data, so downstream
/// components can read <c>Value.Alias</c> and <c>Value.Source</c> directly rather
/// than parsing a display string.</para>
/// </summary>
public class GH_ImageSource : PhyGoo<GH_ImageSource, ImageResource>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ImageSource"/> class with no value.
    /// </summary>
    public GH_ImageSource()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ImageSource"/> class wrapping the given image resource.
    /// </summary>
    /// <param name="resource">The image resource to wrap.</param>
    public GH_ImageSource(ImageResource resource)
        : base(resource)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "Image Source";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "An image plus its alias, carrying raw bytes and MIME type for inline delivery to a multimodal model.";

    /// <inheritdoc/>
    public override bool IsValid => Value is not null;

    /// <inheritdoc/>
    public override IGH_Goo Duplicate() => new GH_ImageSource { Value = Value };

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Value is null)
        {
            return "(empty image source)";
        }

        if (Value.Source is InlineImage inline)
        {
            double kb = inline.Data.Length / 1024.0;
            return $"{Value.Alias} ({inline.MimeType}, {kb:0.#} KB)";
        }

        return $"{Value.Alias} ({Value.Source.GetType().Name})";
    }

    /// <inheritdoc/>
    public override bool CastFrom(object source)
    {
        switch (source)
        {
            case ImageResource resource:
                Value = resource;
                return true;
            case GH_ImageSource goo:
                Value = goo.Value;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public override bool CastTo<Q>(ref Q target)
    {
        // Escape hatch: an image-source wire reads as its alias on any native text input.
        if (Value is not null && typeof(Q).IsAssignableFrom(typeof(GH_String)))
        {
            target = (Q)(object)new GH_String(Value.Alias);
            return true;
        }

        if (Value is not null && typeof(Q).IsAssignableFrom(typeof(string)))
        {
            target = (Q)(object)Value.Alias;
            return true;
        }

        return base.CastTo(ref target);
    }

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: the source component owns persistence (file paths are
    /// re-read into fresh goo on each solve), so the goo itself stores nothing.</remarks>
    public override bool Write(GH_IWriter writer) => true;

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: the source component owns persistence (file paths are
    /// re-read into fresh goo on each solve), so the goo itself stores nothing.</remarks>
    public override bool Read(GH_IReader reader) => true;
}
