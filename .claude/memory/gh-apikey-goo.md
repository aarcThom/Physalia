---
name: gh-apikey-goo
description: "API keys now flow as a typed GH_ApiKey goo (label-only display, never serialized) instead of plain text"
metadata: 
  node_type: memory
  type: project
  originSessionId: f618bb7c-6ad5-49d4-ab22-0abd810aa5f0
---

API keys between the API Keys component and model components now travel as a typed goo, not plain text (landed 2026-06-13).

- **`Goo/GH_ApiKey.cs`** — `GH_ApiKey : PhyGoo<GH_ApiKey, ApiKey>` wrapping the existing Core `Physalia.Core.Config.ApiKey(Provider, Key)` record. `ToString()` → `"<provider> api key"` (secret never shown on canvas). `Write`/`Read` are no-ops → key never serialized into the GH/.ghjson file. `CastFrom` is **strict** (only `ApiKey`/`GH_ApiKey`; a plain-text source fails red, so no raw key can be typed into a doc). `CastTo` yields only the safe label string, never the secret.
- **`Parameters/Param_ApiKey.cs`** — hidden `PhyParam<GH_ApiKey>`, GUID `B7E3D2A9-5C41-4F88-A1D6-9E2F7B3C8A04`.
- Consumers switched from text → `Param_ApiKey`: `ApiKeys` output 0 (emits `new GH_ApiKey(match)`; failure paths emit nothing); `ModelComponentBase` input 0 (Optional, so its own "API key is required" warning stays the single message); `OpenAICompatibleModel` input 1 (Optional — local servers need no key). All read `keyGoo?.Value?.Key`.
- `TokenEstimator` untouched — reads `config.ApiKey` off `GH_ModelConfig`, not the key wire.
- Design decisions confirmed by user: OpenAICompatibleModel switched too (not just ModelComponentBase subclasses); strict — keys originate only from ApiKeys (which reads `API_KEY_CONFIG.YAML`/env), no string cast-in.
- **Breaking:** saved GH files that wired the old *text* API Key output into a model component won't reconnect on load (text→goo mismatch) — wires must be re-drawn.

Mirrors the [[gh-modelconfig-no-serialize]] pattern (GH_ModelConfig also no-ops Write/Read to keep keys out of the file).
