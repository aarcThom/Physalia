// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Providers.Gemini;

namespace Physalia.Core.Providers.Named;

/// <summary>
/// Provider for the Google Gemini API.
/// Speaks the Gemini GenerateContent wire format via <see cref="GeminiProtocolProvider"/>.
/// </summary>
public sealed class GeminiProvider : GeminiProtocolProvider
{
}
