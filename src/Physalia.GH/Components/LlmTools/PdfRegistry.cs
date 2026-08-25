// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel;
using Physalia.Core.Pdf;

namespace Physalia.GH.Components;

/// <summary>
/// The bridge between the two PDF components: the human tool puts files in, the model's
/// <c>read_pdf</c> tool reads them out.
///
/// <para><b>Scoped to a document, not to a component.</b> A harness is one pipeline is one
/// conversation, and the Chat that receives a dropped file and the Read PDF node that answers
/// questions about it both live inside that same sub-document. Walking the wire from a tool node
/// back to a Conversation Log would mean going through the Router and out the other side, which is
/// fragile in a way this is not. A component sitting loose on the canvas still works, because the
/// canvas is then the document both of them share.</para>
///
/// <para><b>Session-only, and nothing is persisted.</b> Same rule the rest of the lifecycle
/// follows. The registry holds paths — PDFs are referenced where they sit, never copied — so a
/// document reopened tomorrow starts with nothing attached and the files are re-picked. A
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed on the document means a closed document
/// takes its registry with it without anything having to remember to clean up.</para>
/// </summary>
internal static class PdfRegistry
{
    private static readonly ConditionalWeakTable<GH_Document, PdfSession> Sessions = new();

    /// <summary>
    /// Returns the session for a document, creating it on first use.
    /// </summary>
    /// <param name="document">The document to scope to.</param>
    /// <returns>The session, or null when there is no document.</returns>
    internal static PdfSession? For(GH_Document? document) =>
        document is null ? null : Sessions.GetValue(document, _ => new PdfSession());
}

/// <summary>
/// The PDFs attached to one document's conversation this session.
/// </summary>
internal sealed class PdfSession
{
    private readonly object _gate = new();
    private readonly List<PdfDescriptor> _attached = new();
    private readonly List<PdfDescriptor> _pending = new();

    /// <summary>
    /// Gets every PDF attached this session, in the order they were added.
    /// </summary>
    /// <returns>The attached documents.</returns>
    internal IReadOnlyList<PdfDescriptor> Attached()
    {
        lock (_gate)
        {
            return _attached.ToList();
        }
    }

    /// <summary>
    /// Gets the PDFs picked but not yet announced in a turn — what the composer shows as chips.
    /// </summary>
    /// <returns>The pending documents.</returns>
    internal IReadOnlyList<PdfDescriptor> Pending()
    {
        lock (_gate)
        {
            return _pending.ToList();
        }
    }

    /// <summary>
    /// Registers a freshly picked PDF, giving it an alias that does not collide with one already in
    /// use, and queues it to be announced with the next prompt.
    /// </summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <returns>The registered descriptor.</returns>
    internal PdfDescriptor Add(string path)
    {
        lock (_gate)
        {
            // Re-picking a file already attached returns what is already there rather than making a
            // second alias for the same document.
            PdfDescriptor? existing = _attached.FirstOrDefault(
                d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                if (!_pending.Contains(existing))
                {
                    _pending.Add(existing);
                }

                return existing;
            }

            string alias = PdfAliases.Unique(
                PdfAliases.FromFileName(path), _attached.Select(d => d.Alias));

            PdfDescriptor descriptor = PdfTextReader.Probe(path, alias);
            _attached.Add(descriptor);
            _pending.Add(descriptor);
            return descriptor;
        }
    }

    /// <summary>
    /// Removes a pending PDF, for the X on its chip. It stays attached and addressable if it was
    /// already announced in an earlier turn — the model has been told about it, so pretending
    /// otherwise would leave it referring to something the tool then denies exists.
    /// </summary>
    /// <param name="alias">The alias to drop.</param>
    internal void RemovePending(string alias)
    {
        lock (_gate)
        {
            _pending.RemoveAll(d => string.Equals(d.Alias, alias, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Takes the pending PDFs and clears the queue, so a prompt announces each attachment once.
    /// </summary>
    /// <returns>The documents to describe in this turn.</returns>
    internal IReadOnlyList<PdfDescriptor> DrainPending()
    {
        lock (_gate)
        {
            List<PdfDescriptor> drained = _pending.ToList();
            _pending.Clear();
            return drained;
        }
    }

    /// <summary>
    /// Finds an attached PDF by alias, tolerating the model quoting it or spelling it with the
    /// original file name.
    /// </summary>
    /// <param name="alias">The alias the model supplied.</param>
    /// <returns>The descriptor, or null when nothing matches.</returns>
    internal PdfDescriptor? Find(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        string wanted = alias.Trim().Trim('`', '"', '\'');

        lock (_gate)
        {
            return _attached.FirstOrDefault(d =>
                       string.Equals(d.Alias, wanted, StringComparison.OrdinalIgnoreCase))
                ?? _attached.FirstOrDefault(d =>
                       string.Equals(d.DisplayName, wanted, StringComparison.OrdinalIgnoreCase))
                ?? _attached.FirstOrDefault(d =>
                       string.Equals(d.Alias, PdfAliases.Sanitize(wanted), StringComparison.OrdinalIgnoreCase));
        }
    }
}
