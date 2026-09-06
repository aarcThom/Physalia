// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Mcp;
using Physalia.Core.Tools;

namespace Physalia.GH.Components;

/// <summary>
/// Connects to one MCP server and advertises its tools to the model.
/// </summary>
/// <remarks>
/// <para><b>One node per connection, one generic class.</b> Nothing here is per-service: the servers
/// come from the user's own configured list, so Physalia ships no catalog and
/// has none to maintain. Place a second node and pick a second server.</para>
/// <para><b>Where the settings live.</b> Which server is picked and which of its tools are advertised
/// are stored on this node, so they travel into a preset. The server's own launch details — command,
/// arguments and, critically, credentials in <c>env</c> — deliberately do NOT: they stay in the
/// user's own file, keyed by name, because a token serialized onto a component would ship inside
/// every preset made from it.</para>
/// <para><b>Why this is the one node advertising many tools.</b> A server's tool set is discovered at
/// runtime, so it cannot be a class per tool. That is what <see cref="Definitions"/> and the Router's
/// set-matching exist for; every other tool node still advertises exactly one.</para>
/// </remarks>
public class McpServer : LlmToolComponentBase, IPickableValuesSource
{
    private const string ServerInputName = "Server";

    private readonly object _gate = new();

    // Server definitions as last read from disk, and the tools last discovered from the picked one.
    private IReadOnlyList<McpServerDefinition> _library = Array.Empty<McpServerDefinition>();

    // Stamp of the file behind _library, so an edit in the chat window (or by another Rhino
    // instance) is picked up without re-parsing on every solve. Null = never read.
    private string? _libraryStamp;
    private IReadOnlyList<LlmToolDefinition> _discovered = Array.Empty<LlmToolDefinition>();

    private string _serverName = string.Empty;
    private string _status = "No server picked.";
    private string? _connectionError;
    private bool _discovering;

    // Whether a listing attempt has completed for the current server, successfully or not. Keyed on
    // "attempted", NOT on the tool count: a server legitimately offering zero tools, or one that
    // cannot be reached at all, would otherwise be re-discovered on every solve — and since each
    // attempt ends by scheduling a solve, that is a loop, not just waste.
    private bool _listed;

    // Null means "never configured — advertise every tool the server has", which is not the same as
    // an empty selection meaning "advertise none". Grasshopper's archive has no null, hence
    // SettingArchive.
    private IReadOnlyList<string>? _toolSelection;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServer"/> class.
    /// </summary>
    public McpServer()
        : base(
            "MCP Server",
            "MCP",
            "Connects to one MCP server you have configured in the chat window and offers its tools to the model. Right-click to pick the server and choose which of its tools to advertise.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("9E4B71C6-2A08-4F3D-B5E7-1C90D8A24F6B");

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput(ServerInputName, _library.Select(s => s.Name).ToList()) };

    /// <summary>Gets the name of the server this node is pointed at, or empty when none is picked.</summary>
    public string ServerName => _serverName;

    /// <summary>
    /// Gets every tool the connected server offers, advertised or not. The chat window's tools page
    /// needs the whole set: a tool switched off must stay listed or there is no way to switch it
    /// back on.
    /// </summary>
    public IReadOnlyList<LlmToolDefinition> DiscoveredTools => _discovered;

    /// <inheritdoc/>
    /// <remarks>
    /// Only the advertised subset, each name prefixed with the server so two servers exporting
    /// <c>search</c> cannot collide at the Router — which keys dispatch by name.
    /// </remarks>
    protected override IReadOnlyList<LlmToolDefinition> Definitions =>
        _discovered
            .Where(IsAdvertised)
            .Select(t => t with { Name = QualifiedName(t.Name) })
            .ToList();

    /// <inheritdoc/>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The model's requests for tools on this server, from a Router output.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Every tool this server offers that you have left switched on, described so the model knows when to call each one.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "Whatever the server answered with. Wire through a Feedback into a Feedback Collector and back to the Router.";

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        // Ending the session here rather than leaving it to the idle reaper matches what the LLM
        // Call does for the CLI providers: deleting the node should stop the process it started.
        McpServerDefinition? definition = Resolve();
        if (definition is not null)
        {
            McpConnections.End(definition);
        }

        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("mcpServerName", _serverName);
        SettingArchive.WriteOptionalNames(writer, "mcpTools", _toolSelection);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _serverName = reader.ItemExists("mcpServerName") ? reader.GetString("mcpServerName") : string.Empty;
        _toolSelection = SettingArchive.ReadOptionalNames(reader, "mcpTools");
        return base.Read(reader);
    }

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        // The library is the source of truth; the Picker only chooses from it.
    }

    /// <inheritdoc/>
    public void ResetValues()
    {
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            ServerInputName,
            "Sv",
            "Which server to connect to. Right-click and add a Picker to choose one; add servers in the chat window.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[1].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Status",
            "St",
            "Whether the server is connected and how many tools it offers. A place to look when a tool is not being called.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);

        Menu_AppendItem(menu, "Reload Servers From File", (_, _) =>
        {
            // Forces both the read and a re-discovery, where the automatic refresh does neither when
            // the stamp is unchanged and this server's own definition did not move.
            _libraryStamp = null;
            ReloadLibrary();
            _listed = false;
            _connectionError = null;
            ExpireSolution(true);
        });

        if (_discovered.Count == 0)
        {
            return;
        }

        Menu_AppendSeparator(menu);
        ToolStripMenuItem tools = Menu_AppendItem(menu, "Tools Advertised To The Model");

        foreach (LlmToolDefinition tool in _discovered)
        {
            string name = tool.Name;
            Menu_AppendItem(tools.DropDown, name, (_, _) => ToggleTool(name), enabled: true, @checked: IsAdvertised(tool));
        }

        Menu_AppendSeparator(tools.DropDown);
        Menu_AppendItem(tools.DropDown, "All", (_, _) =>
        {
            _toolSelection = null;
            ExpireSolution(true);
        });
        Menu_AppendItem(tools.DropDown, "None", (_, _) =>
        {
            _toolSelection = Array.Empty<string>();
            ExpireSolution(true);
        });
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        // Re-read whenever the file has changed, not just when nothing has been read yet. Editing a
        // server in the chat window otherwise left this node on the definition it loaded at startup,
        // with the page showing one thing and the node connecting to another.
        ReloadLibraryIfChanged();

        string picked = string.Empty;
        da.GetData(1, ref picked);

        if (!string.IsNullOrWhiteSpace(picked) && !string.Equals(picked, _serverName, StringComparison.Ordinal))
        {
            _serverName = picked.Trim();
            _discovered = Array.Empty<LlmToolDefinition>();
            _connectionError = null;
            _listed = false;
        }

        McpServerDefinition? definition = Resolve();
        if (definition is null)
        {
            _status = _serverName.Length == 0
                ? "No server picked."
                : $"'{_serverName}' is not one of your configured MCP servers.";
            return;
        }

        McpSession? live = McpConnections.Find(definition);

        // A server that announced notifications/tools/list_changed is re-listed however recently
        // it was listed; otherwise one attempt per server is enough until something changes.
        if (!_listed || live is null || live.ToolsStale)
        {
            BeginDiscovery(definition);
        }

        _status = _connectionError is { } error
            ? $"'{_serverName}': {error}"
            : live is null
                ? $"'{_serverName}': connecting…"
                : $"'{_serverName}': connected, {_discovered.Count} tools ({Definitions.Count} advertised).";
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        da.SetData(FirstAdditionalOutputIndex, _status);

        if (_connectionError is not null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _status);
        }
        else if (_serverName.Length == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Pick a server. Right-click and add a Picker, or type the name of one you configured in the chat window.");
        }
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        McpServerDefinition? definition = Resolve();
        if (definition is null)
        {
            return ToolCallResult.Error($"No MCP server named '{_serverName}' is configured.");
        }

        Result<McpSession, LlmError> connection =
            await McpConnections.GetAsync(definition, BridgeExecutable(), ct).ConfigureAwait(false);

        if (connection.IsErr(out LlmError? error, out McpSession? session))
        {
            return ToolCallResult.Error(error.Message);
        }

        // The model was told the qualified name; the server only knows its own.
        string toolName = LocalName(call.Name);

        Result<McpToolCallResult, LlmError> result =
            await session.CallToolAsync(toolName, call.InputJson, ct).ConfigureAwait(false);

        if (result.IsErr(out LlmError? callError, out McpToolCallResult? value))
        {
            return ToolCallResult.Error(callError.Message);
        }

        return value.IsError
            ? ToolCallResult.Error(value.Text)
            : ToolCallResult.OkWith(value.Text, value.Attachments);
    }

    /// <summary>
    /// Locates the plug-in's MCP server configuration file.
    /// </summary>
    /// <remarks>
    /// <para>Lives in Physalia's per-user data folder (<c>%LOCALAPPDATA%/Physalia</c>) beside the
    /// credential store, as <c>mcp-servers.json</c>. It is written only by the chat window's
    /// "Configure MCP connections" page — the hand-edited YAML it replaced, and the in-place editor
    /// that protected that file's comments and ordering, are both gone.</para>
    /// <para>The shape on disk is the standard <c>mcpServers</c> block, so it is still the thing
    /// every other MCP host reads, and another host's config pastes in whole.</para>
    /// </remarks>
    internal static McpServerStore Store { get; } = CreateStore();

    /// <summary>
    /// Gets where the server list used to live, beside the plug-in.
    /// </summary>
    internal static string LegacyConfigPath
    {
        get
        {
            string? assemblyDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);

            return assemblyDir is null
                ? "MCP_SERVERS.YAML"
                : System.IO.Path.Combine(assemblyDir, "Files", "MCP_SERVERS.YAML");
        }
    }

    // Builds the store and folds in anything left by an older build, once: the YAML that used to sit
    // beside the plug-in, and the one a previous version had already relocated. Both are imported
    // and DELETED — they have been read into a store that supersedes them, and leaving either would
    // mean two lists of servers, credentials in the stale one, that nothing keeps in step.
    private static McpServerStore CreateStore()
    {
        McpServerStore store = McpServerStore.Default();

        try
        {
            string relocatedYaml = System.IO.Path.Combine(
                Physalia.Core.Config.Secrets.SecretStores.DataFolder(), "MCP_SERVERS.YAML");

            foreach (string legacy in new[] { relocatedYaml, LegacyConfigPath })
            {
                IReadOnlyList<string> imported = store.ImportLegacyFile(legacy);
                if (imported.Count > 0)
                {
                    Rhino.RhinoApp.WriteLine(
                        $"[Physalia] Imported {imported.Count} MCP server(s) from {legacy} into {store.FilePath}");
                }
            }
        }
        catch (Exception)
        {
            // A failed import leaves the old file alone; the user re-adds their servers.
        }

        return store;
    }

    /// <summary>
    /// Sets which of the server's tools are advertised. Null restores "all of them".
    /// </summary>
    /// <param name="toolNames">The unqualified tool names to advertise, or null for all.</param>
    internal void SetToolSelection(IReadOnlyList<string>? toolNames)
    {
        _toolSelection = toolNames;
        ExpireSolution(true);
    }

    /// <summary>
    /// Locates the MCP bridge executable, or null when it was not built.
    /// </summary>
    /// <returns>The absolute path to the bridge, or null.</returns>
    /// <remarks>
    /// Absent is not an error until a remote server is actually asked for, which is what makes a
    /// stdio-only install perfectly usable. Internal rather than private because the chat window's
    /// MCP page connects on its own, to run the OAuth sign-in at setup time.
    /// </remarks>
    internal static string? BridgeExecutable()
    {
        string? assemblyDir = System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location);

        if (assemblyDir is null)
        {
            return null;
        }

        string path = System.IO.Path.Combine(assemblyDir, "Bridge", "Physalia.McpBridge.exe");
        return System.IO.File.Exists(path) ? path : null;
    }

    private bool IsAdvertised(LlmToolDefinition tool) =>
        _toolSelection is null || _toolSelection.Contains(tool.Name, StringComparer.OrdinalIgnoreCase);

    private void ToggleTool(string name)
    {
        var selection = new List<string>(_toolSelection ?? _discovered.Select(t => t.Name).ToList());

        if (selection.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            selection.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            selection.Add(name);
        }

        _toolSelection = selection;
        ExpireSolution(true);
    }

    // Prefixed and sanitized: providers accept ^[a-zA-Z0-9_-]{1,64}$ for a tool name, and two
    // servers exporting the same tool would otherwise both answer to one Router key.
    private string QualifiedName(string toolName) =>
        Truncate(Sanitize(_serverName) + "__" + Sanitize(toolName));

    private string LocalName(string qualified)
    {
        string prefix = Sanitize(_serverName) + "__";

        if (qualified.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string stripped = qualified.Substring(prefix.Length);

            // Sanitizing is lossy, so map back through the discovered set rather than trusting the
            // stripped string — a server tool called "read.file" must still be callable.
            LlmToolDefinition? match = _discovered.FirstOrDefault(
                t => string.Equals(Truncate(Sanitize(_serverName) + "__" + Sanitize(t.Name)), qualified, StringComparison.OrdinalIgnoreCase));

            return match?.Name ?? stripped;
        }

        return qualified;
    }

    private static string Sanitize(string value) =>
        new string(value.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());

    private static string Truncate(string value) => value.Length <= 64 ? value : value.Substring(0, 64);

    private McpServerDefinition? Resolve() =>
        _serverName.Length == 0
            ? null
            : _library.FirstOrDefault(s => string.Equals(s.Name, _serverName, StringComparison.OrdinalIgnoreCase));

    // Reads the configured list only when the file's stamp differs from the one behind the copy we
    // hold. Unlike the API node, a reload here can invalidate work: the tools this node advertises
    // were discovered from a PARTICULAR definition, so if the picked server's launch details changed
    // (a new command, a new URL, a different token in env) the discovered set belongs to a server
    // that no longer exists under that name. Comparing Identity is exactly the right test, because it
    // is the same key the connection pool uses to decide whether a warm session may be reused.
    // A stamp change that leaves this server's definition alone — someone edited a DIFFERENT entry —
    // must not reset anything, or every unrelated save would drop a live session's tool list.
    private void ReloadLibraryIfChanged()
    {
        string stamp = Store.RevisionStamp;
        if (stamp == _libraryStamp)
        {
            return;
        }

        string? before = Resolve()?.Identity;
        _libraryStamp = stamp;
        ReloadLibrary();

        if (Resolve()?.Identity != before)
        {
            _discovered = Array.Empty<LlmToolDefinition>();
            _connectionError = null;
            _listed = false;
        }
    }

    private void ReloadLibrary()
    {
        _library = Store.Read();
    }

    // Connect and list off the solve thread, then schedule one solve to publish what came back.
    // Discovery cannot happen inline: it starts a process and waits on a pipe.
    private void BeginDiscovery(McpServerDefinition definition)
    {
        lock (_gate)
        {
            if (_discovering)
            {
                return;
            }

            _discovering = true;
        }

        Task.Run(async () =>
        {
            IReadOnlyList<LlmToolDefinition> tools = Array.Empty<LlmToolDefinition>();
            string? failure = null;

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                Result<McpSession, LlmError> connection =
                    await McpConnections.GetAsync(definition, BridgeExecutable(), timeout.Token).ConfigureAwait(false);

                if (connection.IsErr(out LlmError? error, out McpSession? session))
                {
                    failure = error.Message;
                }
                else
                {
                    Result<IReadOnlyList<LlmToolDefinition>, LlmError> listed =
                        await session.ListToolsAsync(timeout.Token).ConfigureAwait(false);

                    if (listed.IsErr(out LlmError? listError, out IReadOnlyList<LlmToolDefinition>? found))
                    {
                        failure = listError.Message;
                    }
                    else
                    {
                        tools = found;
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            // Publishing happens on the scheduled solve, never here: this is a background thread and
            // the tool set feeds parameter output.
            ScheduleStateSolve(1, () =>
            {
                _discovered = tools;
                _connectionError = failure;

                // Set even on failure: an unreachable server reports itself once and then stops
                // retrying. "Reload Servers From File", or picking a different server, tries again.
                _listed = true;

                lock (_gate)
                {
                    _discovering = false;
                }
            });
        });
    }
}
