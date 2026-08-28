// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Pdf;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// The model-callable <c>read_pdf</c> tool: lists the PDFs available to a conversation, extracts
/// text by page range, finds a term and reports WHERE on the sheet it sits, and rasterizes a page —
/// or a rectangle of one — into an image the model can look at.
///
/// <para>This is the LLM half of a pair. Files reach it two ways: the human drops them into the
/// chat (which needs the Read PDF component under Human Tools wired to the Conversation Log), or
/// they sit in the folder named on this node's own PDF Folder input, which travels inside a preset
/// so a pipeline can ship pointed at a standing reference set.</para>
///
/// <para><b>Why rendering matters here.</b> The PDFs this exists for are architectural drawing
/// sets, and most of what they say is not in the text layer: a section detail, a hatch, a callout
/// bubble, a dimension against a line. Text extraction answers "which sheet" and very little else,
/// so the tool has to be able to hand back a picture. It is also why the render action takes a
/// region — a whole E-size sheet reduced to the delivery cap is legible as a layout and illegible
/// as text, and the way through that is to crop rather than to give up.</para>
/// </summary>
public class ReadPdf : LlmToolComponentBase
{
    private const int InPdfFolder = 1;

    // Long enough for a dense sheet at high DPI, short enough that a pathological file cannot leave
    // a tool call unanswered for the rest of the conversation.
    private const int TimeoutMs = 60_000;

    // A folder pointed at a whole project archive should not stall a solve.
    private const int MaxFolderPdfs = 200;

    // One page per render call, and a bounded text sweep. Both exist to keep a single call from
    // being an expensive way to exhaust a context window.
    private const int MaxTextPages = 20;

    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime Stamp, PdfDescriptor Descriptor)> _folderCache =
        new(StringComparer.OrdinalIgnoreCase);

    private PdfSession? _session;
    private string? _folder;
    private string? _folderWarning;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadPdf"/> class.
    /// </summary>
    public ReadPdf()
        : base(
            "Read PDF",
            "ReadPDF",
            "Lets the model read PDFs the human attached in the chat, or PDFs in a folder you name " +
            "here. It can pull text, search for a phrase and report where on the sheet it sits, and " +
            "render a page — or a crop of one — as an image. Wire the Signal input to a Router.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D4A81F62-37B5-4C09-9E76-8B03C5D21A4F");

    /// <inheritdoc/>
    protected override bool RunsAsync => true;

    /// <inheritdoc/>
    protected override LlmToolDefinition Definition => new(
        "read_pdf",
        "Read a PDF that is attached to this conversation or available in this node's PDF folder. " +
        "Actions: \"list\" reports every PDF available with its page count, sheet size and sheet " +
        "numbers; \"text\" extracts the text layer of a page range; \"search\" finds a phrase and " +
        "reports which page it is on AND where on that page; \"render\" rasterizes a page, or a " +
        "rectangle of a page, and returns it as an image you can look at.\n\n" +
        "These are often architectural drawings, where most of the information is graphical and " +
        "the text layer alone will not answer the question. The way to read a drawing is: render " +
        "the whole page first to see the layout, then render a REGION of it to read any detail. A " +
        "region is {\"x\",\"y\",\"width\",\"height\"} in 0-1 fractions of the page measured from " +
        "the TOP-LEFT corner, and it is rendered at full resolution, so a small crop is far more " +
        "legible than the same area inside a full-page render. \"search\" reports its hits as " +
        "regions in exactly that form, so you can pass one straight back to \"render\".\n\n" +
        "If text on a rendered page is too small to read, that is the scale and not the drawing — " +
        "crop tighter and render again rather than reporting that it cannot be read. If a page has " +
        "no text layer it is a scan or vector-only artwork: render it and look at it.",
        "{\"type\":\"object\",\"properties\":{" +
        "\"action\":{\"type\":\"string\",\"enum\":[\"list\",\"text\",\"search\",\"render\"]," +
        "\"description\":\"What to do. Start with 'list' if you do not know what is available.\"}," +
        "\"alias\":{\"type\":\"string\",\"description\":\"Which PDF, using the alias reported by 'list' or in the attachment notice. Not needed for 'list'.\"}," +
        "\"pages\":{\"type\":\"string\",\"description\":\"For 'text': which pages, e.g. '3', '1-4', '2,5,9', or 'all'.\"}," +
        "\"max_chars\":{\"type\":\"integer\",\"description\":\"For 'text': truncate the extracted text to this many characters.\",\"default\":8000}," +
        "\"query\":{\"type\":\"string\",\"description\":\"For 'search': the phrase to find. Case-insensitive.\"}," +
        "\"page\":{\"type\":\"integer\",\"description\":\"For 'render': the 1-based page to rasterize.\"}," +
        "\"region\":{\"type\":\"object\",\"description\":\"For 'render': the part of the page to rasterize, in 0-1 page fractions from the TOP-LEFT. Omit for the whole page. Crop to read fine print.\"," +
        "\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"width\":{\"type\":\"number\"},\"height\":{\"type\":\"number\"}}," +
        "\"required\":[\"x\",\"y\",\"width\",\"height\"]}," +
        "\"dpi\":{\"type\":\"integer\",\"description\":\"For 'render': resolution, applied to the region rather than the page. Raise it when cropping to read fine print.\",\"default\":150}" +
        "},\"required\":[\"action\"]}");

    /// <inheritdoc/>
    protected override string SignalInputDescription =>
        "Tool calls dispatched from a Router. Wire the Router's read_pdf output here.";

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "The read_pdf tool definition. Wire into a Router, and into a Tools Present component so " +
        "the model is told the tool exists.";

    /// <inheritdoc/>
    protected override string ResultOutputDescription =>
        "The tool result signal. Wire back to the Router's Result input.";

    /// <inheritdoc/>
    protected override void RegisterAdditionalInputs(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter(
            "PDF Folder",
            "PF",
            "A standing set of PDFs the model may always read, on top of anything the human " +
            "attaches in the chat. Either a plain name — a folder under Files/PDFS beside the " +
            "plug-in — or a full path to a folder anywhere, including a network share. Typed here " +
            "rather than derived, so it is saved in the .gh and travels inside a preset. Leave " +
            "blank to use only what the human attaches.",
            GH_ParamAccess.item);
        pManager[InPdfFolder].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterAdditionalOutputs(GH_OutputParamManager pManager) =>
        pManager.AddTextParameter(
            "Available PDFs",
            "P",
            "One line per PDF the model can currently read — what the human has attached this " +
            "session, plus whatever is in the PDF Folder.",
            GH_ParamAccess.list);

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        string typed = string.Empty;
        da.GetData(InPdfFolder, ref typed);

        lock (_gate)
        {
            // Resolved on the solve thread and cached, because the call itself runs off it and
            // must not reach for the document or the param values from a background thread.
            _session = PdfRegistry.For(OnPingDocument());
            _folder = PdfLocations.Resolve(typed);
            _folderWarning = _folder is not null && !Directory.Exists(_folder)
                ? $"The PDF folder \"{_folder}\" does not exist."
                : null;
        }
    }

    /// <inheritdoc/>
    protected override void OnSolveEnd(IGH_DataAccess da)
    {
        if (_folderWarning is not null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _folderWarning);
        }

        // Deliberately does NOT probe. Probing opens and walks every page of every document, and
        // this runs on the solve thread on EVERY solve — pointing the folder input at a job archive
        // would stall Grasshopper for seconds before anyone had asked a question. Attachments are
        // already probed (registration does it, while a human is watching), so they report in full;
        // folder entries report by file name, which costs one directory enumeration. The tool calls
        // themselves probe lazily, off the solve thread, and cache by timestamp.
        var lines = AttachedDocuments()
            .Select(d => $"{d.Alias} — {d.DisplayName} ({d.PageCount} page(s))")
            .ToList();

        string? folder;
        lock (_gate)
        {
            folder = _folder;
        }

        lines.AddRange(PdfLocations.ListPdfs(folder, MaxFolderPdfs)
            .Select(path => $"{PdfAliases.FromFileName(path)} — {Path.GetFileName(path)} (in folder)"));

        da.SetDataList(FirstAdditionalOutputIndex, lines);
    }

    /// <inheritdoc/>
    protected override async Task<ToolCallResult> ExecuteCallAsync(ToolCallContent call, CancellationToken ct)
    {
        PdfToolRequest request = PdfToolRequest.Parse(call.InputJson);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeoutMs);

        try
        {
            // PdfPig and PDFium are both synchronous and both do real work; running them on the
            // pool keeps the solve thread free the way every other async tool here does.
            return await Task.Run(() => Execute(request), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolCallResult.Error(
                "Reading the PDF timed out. Try a narrower page range, a smaller region, or a " +
                "lower dpi.");
        }
    }

    /// <summary>
    /// Runs one parsed request.
    /// </summary>
    /// <param name="request">The parsed call.</param>
    /// <returns>The result to hand back to the model.</returns>
    private ToolCallResult Execute(PdfToolRequest request)
    {
        try
        {
            return request.Action switch
            {
                PdfAction.List => ToolCallResult.Ok(
                    PdfReports.RenderList(AttachedDocuments(), FolderDocuments())),
                PdfAction.Text => ExecuteText(request),
                PdfAction.Search => ExecuteSearch(request),
                PdfAction.Render => ExecuteRender(request),
                _ => ToolCallResult.Error(
                    "read_pdf needs an \"action\" of \"list\", \"text\", \"search\" or \"render\". " +
                    "Call it with action \"list\" to see what is available."),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolCallResult.Error("The PDF could not be read: " + ex.Message);
        }
        catch (Exception ex)
        {
            // A malformed or encrypted PDF surfaces from PdfPig as any of several exception types,
            // none of them worth pinning a catch to. Reporting it as a tool error lets the model
            // move on; letting it escape would fail the whole batch.
            return ToolCallResult.Error(
                "The PDF could not be read — it may be corrupt or password-protected. " + ex.Message);
        }
    }

    /// <summary>
    /// Extracts text for a page selection.
    /// </summary>
    /// <param name="request">The parsed call.</param>
    /// <returns>The result.</returns>
    private ToolCallResult ExecuteText(PdfToolRequest request)
    {
        if (!TryResolve(request.Alias, out PdfDescriptor? document, out ToolCallResult failure))
        {
            return failure;
        }

        IReadOnlyList<int> pages = PdfPageRange.Parse(request.Pages, document!.PageCount, MaxTextPages);
        if (pages.Count == 0)
        {
            return ToolCallResult.Error(
                $"No page in \"{request.Pages}\" exists — {document.DisplayName} has " +
                $"{document.PageCount} page(s).");
        }

        PdfTextResult text = PdfTextReader.ExtractText(document.Path, pages, request.MaxChars);
        return ToolCallResult.Ok(PdfReports.RenderText(document, text, pages));
    }

    /// <summary>
    /// Finds a term and reports where each hit sits.
    /// </summary>
    /// <param name="request">The parsed call.</param>
    /// <returns>The result.</returns>
    private ToolCallResult ExecuteSearch(PdfToolRequest request)
    {
        if (!TryResolve(request.Alias, out PdfDescriptor? document, out ToolCallResult failure))
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return ToolCallResult.Error("read_pdf action \"search\" needs a \"query\" to look for.");
        }

        IReadOnlyList<PdfSearchHit> hits =
            PdfTextReader.Search(document!.Path, request.Query, PdfToolRequest.MaxSearchHits);

        return ToolCallResult.Ok(PdfReports.RenderSearch(document, request.Query, hits));
    }

    /// <summary>
    /// Rasterizes a page, or a region of one, and returns it as an image attachment.
    /// </summary>
    /// <param name="request">The parsed call.</param>
    /// <returns>The result, carrying one image block on success.</returns>
    private ToolCallResult ExecuteRender(PdfToolRequest request)
    {
        if (!TryResolve(request.Alias, out PdfDescriptor? document, out ToolCallResult failure))
        {
            return failure;
        }

        if (request.Page > document!.PageCount)
        {
            return ToolCallResult.Error(
                $"Page {request.Page} does not exist — {document.DisplayName} has " +
                $"{document.PageCount} page(s).");
        }

        PdfRegion region = request.Region ?? PdfRegion.Full;

        if (!PdfPageRenderer.TryRender(
                document.Path, request.Page, region, request.Dpi,
                out PdfRenderedPage? rendered, out string? error))
        {
            return ToolCallResult.Error(error ?? "The page could not be rendered.");
        }

        var attachments = new List<MessageContent>
        {
            new ImageContent(new InlineImage(rendered!.Png, "image/png")),
        };

        return ToolCallResult.OkWith(
            PdfReports.RenderImageReport(
                document, request.Page, region, rendered.Width, rendered.Height, rendered.Downscaled),
            attachments);
    }

    /// <summary>
    /// Resolves the alias a call named, producing a corrective error when it does not match.
    /// </summary>
    /// <param name="alias">The alias the model supplied.</param>
    /// <param name="document">The resolved document.</param>
    /// <param name="failure">The error to return when resolution fails.</param>
    /// <returns>True when a document was resolved.</returns>
    private bool TryResolve(string? alias, out PdfDescriptor? document, out ToolCallResult failure)
    {
        List<PdfDescriptor> available = Available().ToList();
        document = null;
        failure = default;

        if (available.Count == 0)
        {
            failure = ToolCallResult.Error(
                "No PDFs are available. The human has attached none in this conversation, and this " +
                "node's PDF Folder input is empty or contains no PDFs.");
            return false;
        }

        // A single available document with no alias named is unambiguous, and refusing it would be
        // pedantry that costs a round trip.
        if (string.IsNullOrWhiteSpace(alias) && available.Count == 1)
        {
            document = available[0];
            return true;
        }

        document = _session?.Find(alias) ?? FindAmong(available, alias);
        if (document is not null)
        {
            return true;
        }

        failure = ToolCallResult.Error(
            $"No PDF matches the alias \"{alias}\". Available: " +
            string.Join(", ", available.Select(d => d.Alias)) + ".");
        return false;
    }

    /// <summary>
    /// Matches an alias against a list, accepting the alias, the file name, or a sanitized spelling
    /// of either.
    /// </summary>
    /// <param name="documents">The documents to search.</param>
    /// <param name="alias">The alias the model supplied.</param>
    /// <returns>The match, or null.</returns>
    private static PdfDescriptor? FindAmong(IReadOnlyList<PdfDescriptor> documents, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        string wanted = alias.Trim().Trim('`', '"', '\'');
        return documents.FirstOrDefault(d =>
                   string.Equals(d.Alias, wanted, StringComparison.OrdinalIgnoreCase))
            ?? documents.FirstOrDefault(d =>
                   string.Equals(d.DisplayName, wanted, StringComparison.OrdinalIgnoreCase))
            ?? documents.FirstOrDefault(d =>
                   string.Equals(d.Alias, PdfAliases.Sanitize(wanted), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Everything the model may read: attachments first, then the folder.
    /// </summary>
    /// <returns>The available documents.</returns>
    private IReadOnlyList<PdfDescriptor> Available()
    {
        var all = new List<PdfDescriptor>(AttachedDocuments());
        var seen = new HashSet<string>(all.Select(d => d.Alias), StringComparer.OrdinalIgnoreCase);

        foreach (PdfDescriptor d in FolderDocuments())
        {
            if (seen.Add(d.Alias))
            {
                all.Add(d);
            }
        }

        return all;
    }

    /// <summary>
    /// The PDFs the human attached in this conversation.
    /// </summary>
    /// <returns>The attached documents.</returns>
    private IReadOnlyList<PdfDescriptor> AttachedDocuments()
    {
        PdfSession? session;
        lock (_gate)
        {
            session = _session;
        }

        return session?.Attached() ?? Array.Empty<PdfDescriptor>();
    }

    /// <summary>
    /// The PDFs in the configured folder, probed once each and re-probed only when the file on disk
    /// changes. Probing opens and walks every page, so doing it per call on a folder of drawing
    /// sets would dominate the tool's cost.
    /// </summary>
    /// <returns>The folder's documents.</returns>
    private IReadOnlyList<PdfDescriptor> FolderDocuments()
    {
        string? folder;
        lock (_gate)
        {
            folder = _folder;
        }

        if (folder is null)
        {
            return Array.Empty<PdfDescriptor>();
        }

        var documents = new List<PdfDescriptor>();
        var taken = new HashSet<string>(
            AttachedDocuments().Select(d => d.Alias), StringComparer.OrdinalIgnoreCase);

        foreach (string path in PdfLocations.ListPdfs(folder, MaxFolderPdfs))
        {
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(path);

                lock (_gate)
                {
                    if (_folderCache.TryGetValue(path, out (DateTime Stamp, PdfDescriptor Descriptor) hit) &&
                        hit.Stamp == stamp)
                    {
                        documents.Add(hit.Descriptor);
                        taken.Add(hit.Descriptor.Alias);
                        continue;
                    }
                }

                string alias = PdfAliases.Unique(PdfAliases.FromFileName(path), taken);
                PdfDescriptor descriptor = PdfTextReader.Probe(path, alias);
                taken.Add(alias);
                documents.Add(descriptor);

                lock (_gate)
                {
                    _folderCache[path] = (stamp, descriptor);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // One unreadable file in a reference folder must not take the whole listing with it.
            }
        }

        return documents;
    }
}
