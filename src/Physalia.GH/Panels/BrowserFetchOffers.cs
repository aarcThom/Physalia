// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.GH.Harness;

namespace Physalia.GH.Panels;

/// <summary>
/// Files a download could not fetch, offered as a button in the chat window.
///
/// <para><b>Why not the component's right-click menu, which already does this.</b> Because of where
/// the user is standing. A blocked download is discovered mid-conversation: the model explains the
/// problem in the chat and then has to send the person somewhere else — find the Download File node
/// on a canvas they may not be looking at, right-click it, pick a menu item, and paste a URL back in.
/// The menu item is right for an arbitrary URL and stays; it is wrong as the ONLY route out of a
/// failure the chat is already talking about.</para>
///
/// <para>Deliberately a sibling of <c>ToolApprovalBroker</c> rather than part of it. An approval
/// BLOCKS a tool call and must fail closed — no window, no answer, a timeout all deny — because
/// something is waiting on it. An offer blocks nothing: the tool call has already finished and
/// failed, and this is a follow-up the user takes or ignores. Folding the two together would put a
/// timeout and a fail-closed default on something that needs neither, and make the approval
/// invariants harder to read for it.</para>
/// </summary>
internal static class BrowserFetchOffers
{
    // More than a handful means something is retrying against a host that will never serve it; the
    // oldest fall away rather than the chat filling with buttons.
    private const int MaxOffers = 5;

    private static readonly object Gate = new();

    private static readonly List<BrowserFetchOffer> Offers = new();

    /// <summary>
    /// Raised when the offered set changes, so the chat window can push it immediately rather than
    /// on its next tick — the user is reading the model's explanation right now, and the button
    /// belongs beside it.
    /// </summary>
    internal static event Action? Changed;

    /// <summary>
    /// Offers a file the plug-in could not fetch.
    /// </summary>
    /// <param name="url">The address a challenge refused.</param>
    /// <param name="folder">The project folder it should be saved into.</param>
    /// <param name="harness">Which harness asked, for the card's label; may be null.</param>
    internal static void Raise(string url, string folder, HarnessComponent? harness)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        lock (Gate)
        {
            // One offer per URL. A model that retries the same blocked address — which it should not,
            // but which costs nothing to be robust about — must not stack up buttons for one file.
            Offers.RemoveAll(o => string.Equals(o.Url, url, StringComparison.OrdinalIgnoreCase));

            Offers.Add(new BrowserFetchOffer(
                Guid.NewGuid().ToString("N"),
                url,
                folder,
                harness?.NickName));

            while (Offers.Count > MaxOffers)
            {
                Offers.RemoveAt(0);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The offers currently on screen, oldest first.
    /// </summary>
    /// <returns>A snapshot.</returns>
    internal static IReadOnlyList<BrowserFetchOffer> Pending()
    {
        lock (Gate)
        {
            return Offers.ToList();
        }
    }

    /// <summary>
    /// Opens the browser window for one offer and takes it off the list.
    ///
    /// <para>Cleared on being taken rather than on the download completing: the user now has the
    /// window, which reports its own progress, and leaving the button up would invite a second window
    /// on the same file.</para>
    /// </summary>
    /// <param name="id">The offer's id, as the card was given it.</param>
    internal static void Take(string? id)
    {
        BrowserFetchOffer? offer = Remove(id);
        if (offer is not null)
        {
            BrowserFetchWindow.Open(offer.Url, offer.Folder);
        }
    }

    /// <summary>
    /// Drops an offer the user does not want.
    /// </summary>
    /// <param name="id">The offer's id.</param>
    internal static void Dismiss(string? id) => Remove(id);

    private static BrowserFetchOffer? Remove(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        BrowserFetchOffer? found;
        lock (Gate)
        {
            found = Offers.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
            if (found is not null)
            {
                Offers.Remove(found);
            }
        }

        if (found is not null)
        {
            Changed?.Invoke();
        }

        return found;
    }
}

/// <summary>
/// One file the plug-in could not fetch, waiting for the user to fetch it in a browser.
/// </summary>
/// <param name="Id">Identifies this card to the page and back.</param>
/// <param name="Url">The address a challenge refused.</param>
/// <param name="Folder">Where the browser window will save it.</param>
/// <param name="HarnessName">
/// Which harness asked. The window may be showing a different Chat than the pipeline that hit the
/// block, so the card says whose file it is.
/// </param>
internal sealed record BrowserFetchOffer(string Id, string Url, string Folder, string? HarnessName);
