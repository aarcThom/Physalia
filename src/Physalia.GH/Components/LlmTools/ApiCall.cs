// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Api;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Config;

namespace Physalia.GH.Components;

/// <summary>
/// Lets the model read data from one HTTP API the user has configured.
/// </summary>
/// <remarks>
/// <para><b>The model supplies a path and a query, never a URL and never a header.</b> Which API is
/// reachable, where it lives and how it authenticates are set by the human in the chat window; the
/// key is resolved at call time and never passes through anything the model wrote. A tool that took
/// a whole URL would be an open HTTP client with the user's credentials attached, which is a
/// different and much larger thing than access to a data source.</para>
/// <para><b>The answer goes two ways, and that is the point of the node.</b> The full response body
/// lands on the Response output, where the definition can parse it and build from it — data on a
/// wire is what is actually useful in Grasshopper. What goes BACK to the model is a summary: how
/// many records matched, what the fields are called, one record to see the shape of. A conversation
/// is a poor place to keep a data set, because everything put there is paid for on every subsequent
/// turn.</para>
/// <para><b>What the API holds is described here, not in the store.</b> The Description input is the
/// catalog and field list the model reads before deciding to call anything, and it is ordinary
/// internalized param data — so it is saved in the .gh and, more to the point, ships inside a preset.
/// The endpoint's URL and key cannot travel that way and should not; what the data MEANS can and
/// must, or a shared pipeline arrives with its wiring and none of its knowledge. Same reasoning as
/// the Memory tool's folder name and the Read PDF folder.</para>
/// <para><b>Why the description rides in the prompt.</b> It is returned as this node's
/// <see cref="GroundingDirective"/>, so a wired Tools Present grounder folds it into the system
/// prompt rather than leaving it in the tool definition. A tool description is read once the model
/// is already weighing that call; a prompt is read before it decides there is anything to call. The
/// same argument that put the Memory tool's standing instruction in the prompt.</para>
/// </remarks>
public class ApiCall : LlmToolComponentBase, IPickableValuesSource
{
    private const string EndpointInputName = "Endpoint";
    private const int InEndpoint = 1;
    private const int InDescription = 2;
    private const int InMaxRecords = 3;

    private const int OutResponse = 2;
    private const int OutStatus = 3;

    private const int TimeoutMs = 120000;

    // Ceiling on what ONE call may gather, when the endpoint can be paged. Generous enough
    // that a real query is answered in full, small enough that a careless one is not a
    // download. The human raises it on the node; the model can only ask within it.
    private const int DefaultMaxRecords = 1000;

    // Shared, not per-instance: HttpClient is thread-safe and reuse avoids socket exhaustion.
    private static readonly HttpClient HttpClient = new();

    private readonly object _gate = new();

    private readonly ApiKeyResolver _keys = new(PhyCredentials.Store);

    private IReadOnlyList<ApiEndpoint> _library = Array.Empty<ApiEndpoint>();

    // Stamp of the file behind _library, so a change made in the chat window (or by another
    // Rhino instance) is picked up without re-parsing on every solve. Null = never read.
    private string? _libraryStamp;
    private ApiEndpoint? _endpoint;

    private string _endpointName = string.Empty;
    private string _description = string.Empty;
    private IReadOnlyList<string> _lastResponse = Array.Empty<string>();
    private int _maxRecords = DefaultMaxRecords;
    private string _status = "No API picked.";

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiCall"/> class.
    /// </summary>
    public ApiCall()
        : base(
            "API Call",
            "API",
            "Lets the model read data from an HTTP API you have set up in the chat window. It picks the path and the query; the full answer comes out on the Response output for the definition to use.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3C81F5A7-6B24-49E0-9D13-F0A72E5C8B44");

    /// <summary>
    /// Gets the one endpoint store the plug-in reads, shared by every node and by the chat window's
    /// API page — so a save made there is seen here without either side reloading the other.
    /// </summary>
    internal static ApiEndpointStore Store { get; } = ApiEndpointStore.Default();

    /// <inheritdoc/>
    public IReadOnlyList<PickableInput> Inputs =>
        new[] { new PickableInput(EndpointInputName, _library.Select(e => e.Name).ToList()) };

    /// <summary>
    /// Gets the name of the API this node is pointed at, or empty when none is picked.
    /// </summary>
    public string EndpointName => _endpointName;

    /// <inheritdoc/>
    /// <remarks>
    /// The catalog and field list the human wrote, handed to the prompt rather than to the tool
    /// definition. Null when blank, which is what tells the grounder there is nothing to add.
    /// </remarks>
    public override string? GroundingDirective =>
        string.IsNullOrWhiteSpace(_description) || _endpoint is null
            ? null
            : $"The `{ToolName()}` tool reads the {_endpoint.Name} API. {_description.Trim()}";

    /// <inheritdoc/>
    /// <remarks>
    /// Empty until an API is picked. A node advertising a tool it cannot service would be offered to
    /// the model and then fail every call, which reads to the model as a broken API rather than an
    /// unconfigured node.
    /// </remarks>
    protected override IReadOnlyList<LlmToolDefinition> Definitions =>
        _endpoint is null
            ? Array.Empty<LlmToolDefinition>()
            : new[] { new LlmToolDefinition(ToolName(), ToolDescription(_endpoint), ArgumentSchema) };

    /// <inheritdoc/>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "The model's requests to this API, sent here by the Router. A signal made by hand runs the same call, but its answer stays on this node rather than going back to the model.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Advertises this API to the model: a path and a query in, the matching records out. A Tools Present grounder finds this on its own once a Router dispatches here, so it needs no wire.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "The summary heading back to the model. Wire through a Feedback into a Feedback Collector, then into the Router's Results input.";

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("apiEndpointName", _endpointName);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _endpointName = reader.ItemExists("apiEndpointName") ? reader.GetString("apiEndpointName") : string.Empty;
        return base.Read(reader);
    }

    /// <inheritdoc/>
    public void SetValues(string inputName, IEnumerable<string> values)
    {
        // The configured list is the source of truth; the Picker only chooses from it.
    }

    /// <inheritdoc/>
    public void ResetValues()
    {
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            EndpointInputName,
            "E",
            "Which API to call. Right-click and add a Picker to choose one; set APIs up in the chat window.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[InEndpoint].Optional = true;

        pManager.AddTextParameter(
            "Description",
            "D",
            "What this API holds, in your own words — the datasets, the field names, anything the model needs to write a sensible query. This is read before the model decides to call anything, and it travels with the pipeline into a preset.",
            GH_ParamAccess.item,
            string.Empty);
        pManager[InDescription].Optional = true;

        pManager.AddIntegerParameter(
            "Max Records",
            "M",
            "The most records one call may gather when this API supports paging. The model asks for what it wants and this caps it, so a careless query cannot turn into a download. Ignored for an API with no paging configured.",
            GH_ParamAccess.item,
            DefaultMaxRecords);
        pManager[InMaxRecords].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter(
            "Response",
            "R",
            "The full bodies of the last answer, one item per page fetched, in order — every record, not just the page the model happened to read. This is the one the definition should parse; the model only ever sees a summary.",
            GH_ParamAccess.list);

        pManager.AddTextParameter(
            "Status",
            "St",
            "Which API is picked, where its key comes from, and how the last call went. A place to look when a call is failing.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);

        Menu_AppendItem(menu, "Reload APIs From File", (_, _) =>
        {
            // Forces the read even when the stamp says nothing changed — the automatic refresh makes
            // this redundant in ordinary use, and it stays for the case the stamp cannot see (a file
            // rewritten within the file system's write-time resolution).
            _libraryStamp = null;
            ReloadLibraryIfChanged();
            ExpireSolution(true);
        });
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        // Re-read whenever the file has changed, not just when nothing has been read yet. The list
        // has ONE editor — the chat window's API page — and the URL is consulted on every call, so a
        // node holding the endpoint as it was when Rhino started will quietly keep calling the old
        // address while the page shows the new one. The only visible sign of that disagreement is
        // this node's Status output, which is a poor place to discover it. A FileInfo stat per solve
        // is microseconds; re-parsing on every solve would not be, which is what the stamp is for.
        ReloadLibraryIfChanged();

        string picked = string.Empty;
        da.GetData(InEndpoint, ref picked);

        string described = string.Empty;
        da.GetData(InDescription, ref described);
        _description = described ?? string.Empty;

        int cap = DefaultMaxRecords;
        da.GetData(InMaxRecords, ref cap);
        _maxRecords = cap > 0 ? cap : DefaultMaxRecords;

        if (!string.IsNullOrWhiteSpace(picked) && !string.Equals(picked, _endpointName, StringComparison.Ordinal))
        {
            _endpointName = picked.Trim();
        }

        lock (_gate)
        {
            _endpoint = _endpointName.Length == 0
                ? null
                : _library.FirstOrDefault(e => string.Equals(e.Name, _endpointName, StringComparison.OrdinalIgnoreCase));
        }

        if (_endpoint is null)
        {
            _status = _endpointName.Length == 0
                ? "No API picked."
                : $"'{_endpointName}' is not one of your configured APIs.";
            return;
        }

        string source = _keys.SourceOf(_endpoint) switch
        {
            null when _endpoint.NeedsKey => "no key configured",
            null => "no key needed",
            "stored" => "key from the credential store",
            var variable => $"key from {variable}",
        };

        _status = $"{_endpoint.Name} — {_endpoint.BaseUrl} ({source})";
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        // Published here rather than in OnSolveTick, which runs BEFORE the calls and would leave
        // both outputs a solve behind.
        da.SetDataList(OutResponse, _lastResponse);
        da.SetData(OutStatus, _status);
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        ApiEndpoint? endpoint;
        lock (_gate)
        {
            endpoint = _endpoint;
        }

        if (endpoint is null)
        {
            return ToolCallResult.Error(
                "This API node has no API picked, so there is nothing to call. Tell the user to pick one on the node.");
        }

        (string path, string query, int maxChars, int askedFor) = ParseArgs(call.InputJson);

        // The model asks for what it wants; the node's own input is the ceiling. A tool argument is
        // the model's judgement about this question, the input is the human's budget for all of them,
        // so the smaller wins and neither has to know about the other.
        int wanted = Math.Clamp(askedFor, 1, Math.Max(1, _maxRecords));

        string? key = _keys.Resolve(endpoint);
        if (endpoint.NeedsKey && string.IsNullOrWhiteSpace(key))
        {
            return ToolCallResult.Error(
                $"No key is configured for the {endpoint.Name} API. Tell the user to add one in the chat window's API setup page.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeoutMs);

        Result<ApiPagedResponse, LlmError> result = await ApiRequest
            .SendPagedAsync(endpoint, path, query, key, wanted, HttpClient, timeout.Token)
            .ConfigureAwait(false);

        if (result.IsErr(out LlmError? error, out ApiPagedResponse? gathered))
        {
            _status = $"{endpoint.Name}: {error.Message}";
            return ToolCallResult.Error(error.Message);
        }

        _lastResponse = gathered.Pages;

        IReadOnlyList<string> fields = ApiResponseSummary.FieldNames(gathered.Pages.FirstOrDefault());
        string shape = $"{gathered.RecordCount} records over {gathered.Pages.Count} request(s)";
        _status = fields.Count > 0
            ? $"{endpoint.Name}: {shape}, fields {string.Join(", ", fields.Take(6))}"
            : $"{endpoint.Name}: {shape}";

        if (gathered.IsPartial)
        {
            _status += " — partial";
        }

        return ToolCallResult.Ok(ApiResponseSummary.Summarize(gathered, maxChars));
    }

    private static string ArgumentSchema =>
        "{\"type\":\"object\",\"properties\":{"
        + "\"path\":{\"type\":\"string\",\"description\":\"The path to request, relative to the API's configured base URL — for example 'catalog/datasets' or 'catalog/datasets/parking-meters/records'. Never a whole URL.\"},"
        + "\"query\":{\"type\":\"string\",\"description\":\"The query string, without the leading '?' — for example 'where=meterid=\\\"X1\\\"&limit=20'. Leave empty for none.\"},"
        + "\"max_chars\":{\"type\":\"integer\",\"description\":\"How much of the answer to return to you. The full body always goes to the Grasshopper canvas regardless.\",\"default\":4000},"
        + "\"max_records\":{\"type\":\"integer\",\"description\":\"How many records to gather in total. Above one page, the tool follows the API's paging for you and delivers every record to the canvas. Defaults to a single page; raise it when the response says more matched than you received.\",\"default\":100}"
        + "},\"required\":[\"path\"]}";

    private static string ToolDescription(ApiEndpoint endpoint)
    {
        var sb = new StringBuilder();
        sb.Append("Read data from the ").Append(endpoint.Name).Append(" API (GET only, based at ")
          .Append(endpoint.BaseUrl).Append("). ");
        sb.Append("Give a path relative to that base and an optional query string; authentication is handled for you. ");
        sb.Append("You get back a summary — the number of matching records, the field names and one sample record — while the ");
        sb.Append("complete response is delivered to the Grasshopper definition, so ask for what you need to reason about rather ");
        sb.Append("than trying to read a whole data set here.");

        if (endpoint.Paging == ApiPaging.LimitOffset)
        {
            sb.Append(" This API pages its results: set max_records above one page and the tool follows the paging itself, ");
            sb.Append("so the definition receives every record in one call. If the answer says it is not the whole result set, ");
            sb.Append("say so rather than presenting what you got as complete.");
        }

        return sb.ToString();
    }

    private static (string Path, string Query, int MaxChars, int MaxRecords) ParseArgs(string inputJson)
    {
        string path = string.Empty;
        string query = string.Empty;
        int maxChars = 4000;

        // One page unless asked for more. Paging costs real requests against someone's quota, so it
        // is opted into per call; the summary reports the total matched, which is what tells the
        // model there is more to ask for.
        int maxRecords = 100;

        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return (path, query, maxChars, maxRecords);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(inputJson);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("path", out JsonElement pathElement)
                && pathElement.ValueKind == JsonValueKind.String)
            {
                path = pathElement.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("query", out JsonElement queryElement)
                && queryElement.ValueKind == JsonValueKind.String)
            {
                query = queryElement.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("max_chars", out JsonElement maxElement)
                && maxElement.ValueKind == JsonValueKind.Number
                && maxElement.TryGetInt32(out int parsed))
            {
                maxChars = parsed;
            }

            if (root.TryGetProperty("max_records", out JsonElement recordsElement)
                && recordsElement.ValueKind == JsonValueKind.Number
                && recordsElement.TryGetInt32(out int records))
            {
                maxRecords = records;
            }
        }
        catch (JsonException)
        {
            // A malformed argument block leaves the defaults, and the empty path is reported by the
            // request builder in terms the model can act on.
        }

        return (path, query, maxChars, maxRecords);
    }

    // Namespaced by endpoint so two API nodes cannot collide on one Router key, and sanitized to the
    // character set every provider accepts for a tool name. Same rule as the MCP server node.
    private string ToolName()
    {
        string sanitized = new string(
            _endpointName.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());

        string name = "api__" + sanitized;
        return name.Length <= 64 ? name : name.Substring(0, 64);
    }

    // Reads the configured list only when the file's stamp differs from the one behind the copy we
    // hold. The stamp is recorded even when the file is absent, so the FIRST endpoint added shows up
    // here too — that case worked before only by accident, because an empty list was re-read anyway.
    private void ReloadLibraryIfChanged()
    {
        string stamp = Store.RevisionStamp;
        if (stamp == _libraryStamp)
        {
            return;
        }

        _libraryStamp = stamp;
        ReloadLibrary();
    }

    private void ReloadLibrary() => _library = Store.Read();
}
