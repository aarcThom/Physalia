// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Physalia.Core.Parsing;

namespace Physalia.GH.Helpers
{
    /// <summary>
    /// General helper methods dealing directly with Grasshopper documents and components.
    /// </summary>
    public static class GHSystemHelpers
    {
        // PLUGIN ENUMERATION ==========================================================

        /// <summary>
        /// Returns the names of all installed Grasshopper plugins, excluding Grasshopper itself.
        /// </summary>
        /// <returns>A sorted list of installed plugin names.</returns>
        public static List<string> GetInstalledPluginNames()
        {
            return Grasshopper.Instances.ComponentServer.Libraries
                .Where(lib => !lib.Name.Equals("Grasshopper", StringComparison.OrdinalIgnoreCase))
                .Select(lib => lib.Name)
                .OrderBy(n => n)
                .ToList();
        }

        // DOCUMENT DESCRIPTION ========================================================

        /// <summary>
        /// Builds a structured text description of a <see cref="GhDocState"/>, listing its
        /// cluster hooks, components (with wiring), and output connections.
        /// Returns null if the state contains no objects and no hooks.
        /// </summary>
        /// <param name="state">The document state to describe.</param>
        /// <returns>A formatted multi-line string, or null if the state is empty.</returns>
        public static string? DescribeDocumentStructure(GhDocState state)
        {
            if (state.Objects.Count == 0 && state.InputHooks.Count == 0 && state.OutputHooks.Count == 0)
                return null;

            // Assign sequential IDs to non-hook objects for wire source notation.
            var idByGuid = new Dictionary<Guid, string>(state.Objects.Count);
            for (int i = 0; i < state.Objects.Count; i++)
                idByGuid[state.Objects[i].InstanceGuid] = $"comp{i}";

            var sb = new StringBuilder();
            sb.AppendLine($"  Inputs: {string.Join(", ", state.InputHooks.Select(h => h.NickName))}");
            sb.AppendLine($"  Outputs: {string.Join(", ", state.OutputHooks.Select(h => h.NickName))}");

            if (state.Objects.Count > 0)
            {
                sb.AppendLine("  Components:");
                foreach (var obj in state.Objects)
                {
                    string id = idByGuid[obj.InstanceGuid];

                    if (obj is IGH_Component comp)
                    {
                        // Show each input param with its wire source(s) in parentheses.
                        var inputDescs = comp.Params.Input.Select(p =>
                        {
                            var sources = p.Sources
                                .Select(s => SourceLabel(s, idByGuid, state.InputHooks))
                                .ToList();
                            return sources.Count > 0
                                ? $"{p.NickName}(←{string.Join(",", sources)})"
                                : p.NickName;
                        });

                        var outputNames = string.Join(", ", comp.Params.Output.Select(p => p.NickName));
                        sb.AppendLine($"    {id} ({comp.Name} \"{comp.NickName}\"): inputs [{string.Join(", ", inputDescs)}]  outputs [{outputNames}]");
                    }
                    else if (obj is IGH_Param param)
                    {
                        sb.AppendLine($"    {id} (Param \"{param.NickName}\")");
                    }
                }
            }

            sb.AppendLine("  Output wiring:");
            foreach (var hook in state.OutputHooks)
            {
                var sources = hook.Sources
                    .Select(s => SourceLabel(s, idByGuid, state.InputHooks))
                    .ToList();
                sb.AppendLine($"    {hook.NickName} ← {(sources.Count > 0 ? string.Join(", ", sources) : "(none)")}");
            }

            return sb.ToString();
        }

        // DOCUMENT BUILD ==============================================================

        /// <summary>
        /// Resolves all component proxies from the GH component server, adds them to
        /// <paramref name="doc"/>, and returns a <see cref="GhDocState"/> containing lookup
        /// dictionaries for wiring, panel annotation targets for layout, and any errors
        /// encountered during proxy resolution.
        /// </summary>
        /// <param name="doc">The inner GH document to populate.</param>
        /// <param name="defs">The component definitions from the LLM response.</param>
        /// <returns>A populated <see cref="GhDocState"/>.</returns>
        public static GhDocState PlaceObjects(GH_Document doc, List<ClusterComponentDef> defs)
        {
            var byId = new Dictionary<string, IGH_Component>(StringComparer.OrdinalIgnoreCase);
            var paramById = new Dictionary<string, IGH_Param>(StringComparer.OrdinalIgnoreCase);
            var panelTargets = new Dictionary<Guid, Guid>();
            var errors = new List<string>();

            // Pass 1: resolve proxies and separate into IGH_Component vs standalone IGH_Param.
            // All types are resolved before any are added to the document so that the document
            // state is not partially mutated if a type lookup fails.
            var resolvedComponents = new List<(ClusterComponentDef Def, IGH_Component Comp)>();
            var resolvedParams = new List<(ClusterComponentDef Def, IGH_Param Param)>();

            foreach (var def in defs)
            {
                var proxy = Grasshopper.Instances.ComponentServer.FindObjectByName(def.Type, true, true);
                if (proxy == null)
                {
                    errors.Add($"Component type not found: \"{def.Type}\"");
                    continue;
                }

                var obj = proxy.CreateInstance();

                if (obj is GH_Panel panel)
                {
                    if (!string.IsNullOrWhiteSpace(def.Nickname))
                        panel.UserText = def.Nickname;
                    resolvedParams.Add((def, panel));
                }
                else if (obj is IGH_Param param)
                {
                    SetParamValue(param, def.Nickname);
                    resolvedParams.Add((def, param));
                }
                else if (obj is IGH_Component comp)
                {
                    if (def.Nickname != null)
                        comp.NickName = def.Nickname;
                    resolvedComponents.Add((def, comp));
                }
                else
                {
                    errors.Add($"\"{def.Type}\" is not a usable GH object");
                }
            }

            // Pass 2: add IGH_Component objects to the document.
            // HierarchicalLayout.Apply will assign final positions after wiring is complete.
            var objects = new List<IGH_DocumentObject>(resolvedComponents.Count + resolvedParams.Count);
            foreach (var (def, comp) in resolvedComponents)
            {
                comp.CreateAttributes();
                doc.AddObject(comp, false);
                byId[def.Id] = comp;
                objects.Add(comp);
            }

            // Pass 3: add standalone params and build panel annotation targets.
            // Panel targets map a panel's InstanceGuid → the component it annotates, used by
            // HierarchicalLayout to position it beside the target.
            foreach (var (def, param) in resolvedParams)
            {
                param.CreateAttributes();
                doc.AddObject(param, false);
                paramById[def.Id] = param;
                objects.Add(param);

                if (def.Annotates != null && byId.TryGetValue(def.Annotates, out var targetComp))
                    panelTargets[param.InstanceGuid] = targetComp.InstanceGuid;
            }

            // Capture hooks from the document (placed by SyncHooks before PlaceObjects was called).
            IGH_Param[] inputHooks = doc.ClusterInputHooks() ?? Array.Empty<GH_ClusterInputHook>();
            IGH_Param[] outputHooks = doc.ClusterOutputHooks() ?? Array.Empty<GH_ClusterOutputHook>();

            var state = new GhDocState(objects, inputHooks, outputHooks, byId, paramById, panelTargets);
            state.Errors.AddRange(errors);
            return state;
        }

        /// <summary>
        /// Wires each component's inputs according to the LLM-specified wire sources.
        /// Missing target params or unresolvable sources are added to <see cref="GhDocState.Errors"/>.
        /// </summary>
        /// <param name="defs">The component definitions from the LLM response.</param>
        /// <param name="state">The build state produced by <see cref="PlaceObjects"/>.</param>
        public static void WireComponentInputs(List<ClusterComponentDef> defs, GhDocState state)
        {
            foreach (var def in defs)
            {
                // Skip defs that were not successfully placed (type not found).
                if (!state.ById.TryGetValue(def.Id, out var comp))
                    continue;

                foreach (var (nickName, source) in def.Inputs)
                {
                    // Find the target input param by NickName (case-insensitive fallback).
                    var targetParam = comp.Params.Input.FirstOrDefault(p => p.NickName == nickName)
                        ?? comp.Params.Input.FirstOrDefault(p => string.Equals(p.NickName, nickName, StringComparison.OrdinalIgnoreCase))
                        ?? comp.Params.Input.FirstOrDefault(p => string.Equals(p.Name, nickName, StringComparison.OrdinalIgnoreCase));

                    if (targetParam == null)
                    {
                        var available = string.Join(", ", comp.Params.Input.Select(p => p.NickName));
                        state.Errors.Add($"Component \"{def.Id}\" ({def.Type}) has no input \"{nickName}\". Available: [{available}]");
                        continue;
                    }

                    var sourceParam = ResolveSource(source, state);
                    if (sourceParam == null)
                    {
                        AddWireSourceError(source, state);
                        continue;
                    }

                    targetParam.AddSource(sourceParam);
                }
            }
        }

        /// <summary>
        /// Wires each cluster output hook to its source according to the LLM-specified wire sources.
        /// Missing hooks or unresolvable sources are added to <see cref="GhDocState.Errors"/>.
        /// </summary>
        /// <param name="outputs">The output definitions from the LLM response.</param>
        /// <param name="state">The build state produced by <see cref="PlaceObjects"/>.</param>
        public static void WireOutputHooks(List<ClusterOutputDef> outputs, GhDocState state)
        {
            foreach (var outDef in outputs)
            {
                var hook = state.OutputHooks.FirstOrDefault(h => h.NickName == outDef.Name);
                if (hook == null)
                {
                    var available = string.Join(", ", state.OutputHooks.Select(h => $"\"{h.NickName}\""));
                    state.Errors.Add($"Cluster output hook not found: \"{outDef.Name}\". Available: [{available}]");
                    continue;
                }

                var sourceParam = ResolveSource(outDef.From, state);
                if (sourceParam == null)
                {
                    AddWireSourceError(outDef.From, state);
                    continue;
                }

                hook.AddSource(sourceParam);
            }
        }

        // RUNTIME ERROR COLLECTION ====================================================

        /// <summary>
        /// Collects all runtime errors and warnings from every active object in <paramref name="doc"/>.
        /// Each entry pairs the severity level with a formatted message string "[NickName] message",
        /// so callers can both store the text and surface it on a bubble at the correct level.
        /// </summary>
        /// <param name="doc">The GH document to inspect.</param>
        /// <returns>A list of (level, message) pairs; empty if there are no runtime messages.</returns>
        public static List<(GH_RuntimeMessageLevel Level, string Message)> CollectErrors(GH_Document doc)
        {
            var results = new List<(GH_RuntimeMessageLevel Level, string Message)>();
            var levels = new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning };

            foreach (var obj in doc.Objects)
            {
                if (obj is not IGH_ActiveObject active)
                    continue;

                foreach (var level in levels)
                {
                    foreach (var msg in active.RuntimeMessages(level))
                        results.Add((level, $"[{obj.NickName}] {msg}"));
                }
            }

            return results;
        }

        // PRIVATE HELPERS =============================================================

        // Returns a human-readable label for a wire source parameter.
        // "input.name" for cluster input hooks; "compN.nickName" for component outputs;
        // "compN" for standalone params (Panel, Param_Number, etc.).
        private static string SourceLabel(IGH_Param source, Dictionary<Guid, string> idByGuid, IReadOnlyList<IGH_Param> inputHooks)
        {
            // Check if it's a cluster input hook.
            if (inputHooks.Any(h => h.InstanceGuid == source.InstanceGuid))
                return $"input.{source.NickName}";

            // For a component output param, GetTopLevel.DocObject returns the owning component.
            var ownerObj = source.Attributes?.GetTopLevel?.DocObject;
            if (ownerObj != null && idByGuid.TryGetValue(ownerObj.InstanceGuid, out var ownerId))
                return $"{ownerId}.{source.NickName}";

            // Standalone param (e.g. Panel, Param_Number) — the param itself is the object.
            if (idByGuid.TryGetValue(source.InstanceGuid, out var paramId))
                return paramId;

            return source.NickName;
        }

        // Resolves a wire source string ("input.<name>" or "<id>.<nickName>") to the
        // corresponding IGH_Param using the lookup tables in state.
        private static IGH_Param? ResolveSource(string source, GhDocState state)
        {
            int dot = source.IndexOf('.');
            if (dot < 0)
                return null;

            string id = source[..dot];
            string nickName = source[(dot + 1)..];

            if (id.Equals("input", StringComparison.OrdinalIgnoreCase))
                return state.InputHooks.FirstOrDefault(h => h.NickName == nickName);

            // Panels and other standalone params are themselves the output; nickName is ignored.
            if (state.ParamById.TryGetValue(id, out var param))
                return param;

            if (state.ById.TryGetValue(id, out var comp))
                return comp.Params.Output.FirstOrDefault(p => p.NickName == nickName)
                    ?? comp.Params.Output.FirstOrDefault(p => string.Equals(p.NickName, nickName, StringComparison.OrdinalIgnoreCase))
                    ?? comp.Params.Output.FirstOrDefault(p => string.Equals(p.Name, nickName, StringComparison.OrdinalIgnoreCase));

            return null;
        }

        // Adds a descriptive wire-source error to state.Errors.
        // If the source component ID is found, the available output NickNames are listed so the
        // LLM can self-correct the output param name on the next autofix pass.
        private static void AddWireSourceError(string source, GhDocState state)
        {
            int srcDot = source.IndexOf('.');
            string srcId = srcDot >= 0 ? source[..srcDot] : source;

            if (state.ById.TryGetValue(srcId, out var srcComp))
            {
                var available = string.Join(", ", srcComp.Params.Output.Select(p => p.NickName));
                state.Errors.Add($"Wire source \"{source}\" not found. \"{srcId}\" ({srcComp.Name}) has outputs: [{available}]");
            }
            else
            {
                state.Errors.Add($"Wire source not found: \"{source}\"");
            }
        }

        // Sets persistent data on a standalone parameter component from the nickname string value.
        private static void SetParamValue(IGH_Param param, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (param is Param_Number numP
                && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                numP.SetPersistentData(new GH_Number(d));
            else if (param is Param_Integer intP
                && int.TryParse(value, out int i))
                intP.SetPersistentData(new GH_Integer(i));
            else if (param is Param_Boolean boolP)
                boolP.SetPersistentData(new GH_Boolean(
                    value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1"));
            else if (param is Param_String strP)
                strP.SetPersistentData(new GH_String(value));

            param.NickName = value;
        }
    }
}
