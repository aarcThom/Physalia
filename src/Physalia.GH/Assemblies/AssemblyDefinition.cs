// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Physalia.GH.Assemblies;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AssemblyComponentDef), "component")]
[JsonDerivedType(typeof(AssemblyParamDef), "param")]
[JsonDerivedType(typeof(AssemblyGroupDef), "group")]
[JsonDerivedType(typeof(AssemblyScribbleDef), "scribble")]
public abstract record AssemblyObjectDef(string Id);

public record AssemblyComponentDef(
    string Id,
    Guid TypeGuid,
    string Nickname,
    float PivotX,
    float PivotY) : AssemblyObjectDef(Id);

public record AssemblyParamDef(
    string Id,
    Guid TypeGuid,
    string Nickname,
    float PivotX,
    float PivotY) : AssemblyObjectDef(Id);

/// <summary>
/// Group bounds are auto-computed from members on import; no pivot is stored.
/// </summary>
public record AssemblyGroupDef(
    string Id,
    string Label,
    string Colour,
    List<string> Members) : AssemblyObjectDef(Id);

public record AssemblyScribbleDef(
    string Id,
    string Text,
    float PivotX,
    float PivotY) : AssemblyObjectDef(Id);

public record AssemblyWire(
    string FromId,
    int FromOutputIndex,
    string ToId,
    int ToInputIndex);

public record AssemblyExposedPort(
    string ComponentId,
    bool IsInput,
    int ParamIndex,
    string Label);

public record AssemblyDefinition(
    string Name,
    List<AssemblyObjectDef> Objects,
    List<AssemblyWire> Wires,
    List<AssemblyExposedPort> ExposedInputs,
    List<AssemblyExposedPort> ExposedOutputs);
