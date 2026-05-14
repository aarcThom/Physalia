// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Conversations;

/// <summary>
/// Discriminated union describing where image bytes come from.
/// Each provider adapter maps the concrete subtype to its own wire format.
/// </summary>
public abstract record ImageSource;

/// <summary>
/// Image delivered as raw bytes with an explicit MIME type.
/// Supported by all providers.
/// </summary>
/// <param name="Data">Raw image bytes.</param>
/// <param name="MimeType">MIME type, e.g. "image/jpeg".</param>
public record InlineImage(byte[] Data, string MimeType) : ImageSource;

/// <summary>
/// Image delivered by public URL.
/// Supported by OpenAI and Anthropic; Gemini requires a GCS or Files API URI instead.
/// </summary>
/// <param name="Url">Publicly accessible image URL.</param>
public record UrlImage(string Url) : ImageSource;

/// <summary>
/// Image previously uploaded via a provider Files API, referenced by its opaque handle.
/// Anthropic: indefinite persistence. Gemini: 48-hour TTL.
/// OpenAI Chat Completions does not support a Files API — reject or fall back to inline.
/// </summary>
/// <param name="FileHandle">Provider-issued file identifier.</param>
public record ManagedImage(string FileHandle) : ImageSource;
