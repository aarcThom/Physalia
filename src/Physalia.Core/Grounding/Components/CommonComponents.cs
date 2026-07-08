// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;

namespace Physalia.Core.Grounding.Components;

/// <summary>
/// The curated set of common Grasshopper component names whose typed signatures are always folded
/// into the component-catalog grounding, even when full signature exposure is off. Signatures for
/// this subset cost a few thousand tokens and eliminate the model's most damaging failure mode —
/// guessing a common component's parameter order. Names that are not installed simply never match,
/// so plug-in-free and stripped-down installs are unaffected. Keep this list in sync with the
/// componentCatalog block in Files/SYSTEM_PROMPTS/SCHEMA/Node Graph.json.
/// </summary>
public static class CommonComponents
{
    /// <summary>
    /// Gets the curated common-component names. Matching is case-insensitive; entries must use the
    /// component's exact Grasshopper library name.
    /// </summary>
    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Params / sources
        "Number Slider", "Boolean Toggle", "Panel", "Value List", "Button", "MD Slider",
        "Point", "Vector", "Plane", "Curve", "Surface", "Brep", "Mesh", "Geometry",
        "Number", "Integer", "Boolean", "Text", "Domain", "Colour Swatch", "Graph Mapper",

        // Maths
        "Addition", "Subtraction", "Multiplication", "Division", "Negative", "Absolute Value",
        "Power", "Square Root", "Modulus", "Maximum", "Minimum", "Average", "Round", "Pi",
        "Random", "Series", "Range", "Construct Domain", "Deconstruct Domain", "Bounds",
        "Remap Numbers", "Expression", "Larger Than", "Smaller Than", "Equality",
        "Gate And", "Gate Or", "Gate Not",

        // Sets / tree
        "Merge", "Entwine", "List Item", "List Length", "Reverse List", "Sort List",
        "Shift List", "Cull Pattern", "Cull Index", "Dispatch", "Weave", "Partition List",
        "Split List", "Duplicate Data", "Flatten Tree", "Graft Tree", "Simplify Tree",
        "Flip Matrix", "Stream Filter", "Stream Gate",

        // Vector / point / plane
        "Construct Point", "Deconstruct", "Vector XYZ", "Deconstruct Vector", "Unit X",
        "Unit Y", "Unit Z", "Amplitude", "Vector 2Pt", "Distance", "XY Plane", "XZ Plane",
        "YZ Plane", "Construct Plane", "Plane Normal", "Deconstruct Plane",

        // Curve
        "Line", "Line SDL", "Circle", "Circle CNR", "Arc", "Arc 3Pt", "Rectangle",
        "Rectangle 2Pt", "Rectangle 3Pt", "Polygon", "Ellipse", "Polyline", "Interpolate",
        "Nurbs Curve", "Divide Curve", "Evaluate Curve", "End Points", "Curve Middle",
        "Length", "Offset Curve", "Join Curves", "Explode", "Flip Curve", "Perp Frames",
        "Horizontal Frames", "Curve Closest Point", "Shatter", "Fillet",

        // Surface / solid
        "Extrude", "Loft", "Sweep1", "Sweep2", "Revolution", "Boundary Surfaces",
        "Ruled Surface", "Network Surface", "Pipe", "Sphere", "Box 2Pt", "Center Box",
        "Domain Box", "Box Rectangle", "Cylinder", "Cone", "Plane Surface",
        "Deconstruct Brep", "Brep Join", "Cap Holes", "Offset Surface", "Divide Surface",
        "Evaluate Surface", "Isotrim", "Surface Closest Point", "Area", "Volume",
        "Solid Union", "Solid Difference", "Solid Intersection", "Region Union",
        "Region Difference",

        // Mesh
        "Construct Mesh", "Deconstruct Mesh", "Mesh Brep",

        // Transform
        "Move", "Rotate", "Rotate Axis", "Scale", "Scale NU", "Mirror", "Orient", "Project",
        "Linear Array", "Polar Array", "Rectangular Array",

        // Intersect / display
        "Curve | Curve", "Brep | Brep", "Brep | Curve", "Contour", "Custom Preview",
        "Colour RGB",
    };
}
