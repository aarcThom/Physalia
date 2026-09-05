# Model API credentials — encrypted store, UI-owned setup, one `Model API` object

Status: **BUILT 2026-09-04**, and the legacy YAML path was **stripped out entirely** the same day
(see §10). Compiles clean (`dotnet build src/Physalia.slnx -c Debug`), 669 Core
tests pass, **not yet run inside Rhino**. Supersedes the plaintext `Files/API_KEY_CONFIG.YAML` as the
*primary* path (the file survives, demoted — see Migration).

**Option B was taken** (no user `.gh` files existed): `OpenAICompatibleModel` dropped its `Base URL`
input and took a new ComponentGuid. See "What this cost" at the foot of this document.

---

## What this changes, and why

Two moves that only make sense together.

**1. The setup UI becomes the authoring surface, so the store can be opaque.**
Today `API_KEY_CONFIG.YAML` is hand-editable, which is exactly why encrypting it would be wrong —
you would destroy the thing it is for. Once the chat window owns authoring, nobody edits the store,
and encryption costs nothing. The credential moves to `%LOCALAPPDATA%/Physalia/credentials.dat`,
DPAPI-wrapped, beside the MCP token cache that already works this way.

**2. A key and its endpoint are one fact, so they become one object.**
`https://api.openai.com/v1` plus a key is not two settings a user assembles; it is one provider.
Today the OpenAI-compatible node makes you wire them separately and know the URL by heart. For the
three providers being added this is not a nicety — Alibaba, Z.AI and Moonshot are all
OpenAI-compatible with *different* base URLs, so a key alone is meaningless. `GH_ApiKey` becomes
`GH_ModelApi` carrying `(Provider, BaseUrl, Key)`.

**What it buys.** Honestly: it stops another local account reading your keys, and keeps plaintext
secrets out of backups, screenshots and support bundles. It does not stop code running as the user —
that calls `CryptUnprotectData` exactly as ours does. Accidental disclosure is how keys actually
leak, so this is worth having. The larger win is UX: paste into a dialog, never learn a YAML file
exists.

---

## 1. The credential store (Core)

New, under `Physalia.Core/Config/`:

- **`ISecretStore`** — `TryRead(out string json)` / `Write(string json)`. One abstraction, three
  implementations chosen at construction: **`DpapiSecretStore`** (`DataProtectionScope.CurrentUser`)
  on Windows, **`KeychainSecretStore`** on macOS, plaintext-with-owner-only-mode elsewhere.
- **`CredentialStore`** — the typed layer over it. Document shape:

```json
{ "version": 1,
  "providers": {
    "anthropic": { "url": "https://api.anthropic.com/v1", "key": "sk-ant-..." },
    "moonshot":  { "url": "https://api.moonshot.ai/v1",   "key": "sk-..." }
  } }
```

- **`ModelApi(string Provider, string BaseUrl, string Key)`** replaces `ApiKey`. `Key` may be empty
  (a local endpoint that asks for none); `BaseUrl` may be empty (a tool key like Tavily).
- **`ModelApiResolver`** — the single read path, used by the `Model API` component, `WebToolKeys` and
  `ProviderAvailability` so the three cannot disagree. Order:

  1. **Environment variable** — still wins, and stays first. It is the path for headless, CI and
     shared team setups, and for anyone who refuses secrets on disk at all. Names come from a static
     table keyed by provider id (`ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `DASHSCOPE_API_KEY`,
     `MOONSHOT_API_KEY`, …), plus any custom names still declared in the YAML's `env_vars`.
  2. **Encrypted store.**
  3. **`API_KEY_CONFIG.YAML` inline values** — legacy, and the escape hatch for anyone who wants to
     manage keys their own way.

### Two things that will bite

- **Decrypt once, not per solve.** `ApiKeys.SolveInstance` re-reads the config on *every* solve to
  keep the Picker's provider list live (`ApiKeys.cs:88`). With DPAPI that becomes a decrypt per solve
  per node. Cache with an mtime check — copy `FileTokenCache`'s `_cached`/`_loaded` reasoning, which
  documents this exact hot-path problem, rather than re-deriving it.
- **Portability breaks silently.** `CurrentUser` scope means the file copies to a new machine and
  simply fails to decrypt. The MCP token cache does not care — re-auth is one browser click — but
  re-entering eight API keys is not. The failure must read *"credentials found but encrypted by a
  different account"*, distinct from *"nothing configured"*, or the setup screen will claim the
  user's config vanished.

### Mac, and don't build it twice

`FileTokenCache` already carries a Windows/plaintext split, and CLAUDE.md flags Keychain as the real
Mac answer. Build `ISecretStore` once and **retrofit the bridge onto it**. Note the bridge has no
`ProjectReference` to Core by design (leaf net8.0 exe — see the comment in `Physalia.McpBridge.csproj`),
so sharing means a linked `<Compile Include="..\Physalia.Core\Config\DpapiSecretStore.cs" Link="..." />`,
not a project reference. Decide that up front rather than discovering it mid-refactor.

---

## 2. The `Model API` object (GH)

| Today | After |
|---|---|
| `ApiKey` (Core record) | `ModelApi(Provider, BaseUrl, Key)` |
| `GH_ApiKey` / `Param_ApiKey` | `GH_ModelApi` / `Param_ModelApi` |
| `ApiKeys` component ("API Keys") | **"Model API"** — same class, **ComponentGuid pinned** |

`GH_ModelApi` keeps `GH_ApiKey`'s discipline verbatim: label-only `ToString()` (`"anthropic api"`),
`Write` a no-op, strict `CastFrom` so a raw string can never become a credential. Nothing downstream
of the model nodes changes.

**Every `ModelConfig` already carries a `BaseUrl`** with a default (`AnthropicConfig.cs:31`,
`GeminiConfig.cs:27`, `OpenAICompatibleConfig.cs:36`) — it is simply not exposed as an input on the
Anthropic and Gemini nodes. So `ModelApi` maps onto all of them with no Core config change, and those
two nodes gain endpoint-swapping for free. Not academic: Z.AI and Moonshot both publish
Anthropic-compatible endpoints.

### The saved-file trap — read before touching params

Grasshopper restores component params **by index**. `OpenAICompatibleModel` is today
`0=Base URL(text) 1=API Key 2=Model 3=Max Tokens`. Deleting input 0 shifts every other index and
mis-restores every saved `.gh` and every shipped preset.

**Option B was chosen and built.** `OpenAICompatibleModel` is now
`0=Model API, 1=Model, 2=Max Tokens`, with a new ComponentGuid
(`5A3E9D41-7C28-4B06-9E5F-1D84C0B7A2E6`). The old id is retired rather than reused, because reusing
it would restore an archived four-param layout onto a three-param component: the Picker that was on
old index 2 (Model) would land on new index 2 (Max Tokens). A clean "component not found" placeholder
is a better failure than a graph that loads and silently means something else.

The same reasoning retired `Param_ApiKey`'s GUID and the `ApiKeys` component's — the type on the wire
changed shape, so there is no archive worth pretending to be compatible with.

---

## 3. The setup page

Replaces "paste the key into the message box below and press Enter" — today the key rides the
composer, which is why `HandleSaveKey` exists as a URL verb (`ChatWindow.cs:908`).

**A provider's guide page gets exactly one of two footers**, driven by new fields on `Provider`:

- **Detect providers** (`claude-code`, `codex`, `local-llm`) — a single button: **"Detect Claude
  Code"** / **"Detect Codex"** / **"Detect local server"**. Click, and the host runs
  `ClaudeCodeProvider.IsCliAvailable()` / `CodexProvider.IsCliAvailable()` / the llama-server probe,
  reports the outcome inline, and on success the provider joins the configured list.

  *Design point worth settling deliberately:* configured-ness for these is **derived from a live
  probe** (`ProviderAvailability`), never stored. Keep it that way — the button forces an immediate
  re-probe rather than writing a flag, so a CLI that is later uninstalled drops off honestly instead
  of the pill lying about it. What the button really replaces is today's silent background polling,
  which gives the user nothing to act on.

- **Key providers** — two boxes: **API URL** (prefilled with that provider's default) and
  **API key**, plus Save. Stored as one `ModelApi` entry.

  The shape varies by provider and the form must respect it:

  | Provider | URL box | Key box |
  |---|---|---|
  | Anthropic, Google, OpenAI, DeepSeek, OpenRouter, Alibaba, Z.AI, Moonshot | yes (prefilled) | yes |
  | `other` | yes (empty) | optional |
  | Tavily, Jina (`kind: 'tool'`) | **no** | yes |

`Provider` in `providers.ts` gains `needsUrl: boolean`, `defaultUrl?: string` and
`detect?: { label: string }`. `needsKey` stays as it is.

**Bridge verbs:** `savekey?provider=…&key=…` gains `&url=…` (or becomes `saveprovider`); new
`detect?provider=…`. Both return the existing `SetupResult` shape, so `Setup.svelte`'s result banner
is reused unchanged.

**One vocabulary end to end.** Provider ids become the store's keys, which deletes both mapping
tables that exist today purely to translate between YAML section/leaf and setup id —
`ChatWindow.KeyTargets` (`ChatWindow.cs:110`) and `ProviderAvailability.KeyProviderToSetupId`. Those
two must agree today, and nothing enforces it.

---

## 4. The three new providers

Endpoints verified against vendor docs, September 2026:

| id | Label | Default API URL | Notes |
|---|---|---|---|
| `alibaba` | Alibaba Cloud (Qwen) | `https://dashscope-intl.aliyuncs.com/compatible-mode/v1` | Beijing is `https://dashscope.aliyuncs.com/…`, Virginia `https://dashscope-us.aliyuncs.com/…`. Env var `DASHSCOPE_API_KEY`. |
| `zai` | Z.AI (GLM) | `https://api.z.ai/api/paas/v4` | **A Coding Plan key needs `https://api.z.ai/api/coding/paas/v4` instead — the two endpoints are not interchangeable.** Say so in the guide steps. |
| `moonshot` | Moonshot AI (Kimi) | `https://api.moonshot.ai/v1` | `.cn` for the mainland platform. An Anthropic-compatible endpoint also exists at `/anthropic`. |

All three ride `OpenAICompatibleProvider` on a base-URL swap — no new provider classes, per the
provider-as-adapter rule. Each needs a `providers.ts` entry with steps, console links and a blurb in
the voice of the existing entries.

That regional-endpoint variance is the strongest single argument for this redesign: with a prefilled,
editable URL box the user changes one field. Under today's design they must know to leave the setup
screen and hand-edit a YAML file.

---

## 5. Migration from the YAML

- On first run, if `API_KEY_CONFIG.YAML` holds inline `api_keys` values, offer to move them into the
  store and blank those lines. **Edit, never regenerate** — replace only the value lines, preserving
  comments, ordering and the user's own notes, exactly as `McpConfigEditor` does for
  `MCP_SERVERS.YAML`.
- `env_vars` mappings **stay in the file**. They are variable *names*, not secrets, and they are how
  a user keeps credentials off disk entirely.
- `Api.SetKey` loses its only caller and can be retired once migration ships. `Api.GetKeys` remains as
  resolver step 3.
- `.gitignore` entries stay exactly as they are.

---

## 6. Files touched

| File | Change |
|---|---|
| `Physalia.Core/Config/ISecretStore.cs`, `DpapiSecretStore.cs`, `KeychainSecretStore.cs` | new |
| `Physalia.Core/Config/CredentialStore.cs`, `ModelApi.cs`, `ModelApiResolver.cs` | new |
| `Physalia.Core/Config/ApiKey.cs` | superseded by `ModelApi` |
| `Physalia.Core/Config/Api.cs` | demoted to legacy read; `SetKey` retired after migration |
| `Physalia.GH/Goo/GH_ApiKey.cs` → `GH_ModelApi.cs` | rename, plus `BaseUrl` |
| `Physalia.GH/Parameters/Param_ApiKey.cs` → `Param_ModelApi.cs` | rename, **GUID pinned** |
| `Physalia.GH/Components/Models/ApiKeys.cs` | → "Model API"; GUID pinned; cached read |
| `Physalia.GH/Components/Models/ModelComponentBase.cs` | input type at index 0 |
| `Physalia.GH/Components/Models/OpenAICompatibleModel.cs` | index 1 type; Base URL becomes an override |
| `Physalia.GH/Components/LlmTools/WebToolKeys.cs` | route through the resolver |
| `Physalia.GH/Panels/ProviderAvailability.cs` | read the store; drop `KeyProviderToSetupId` |
| `Physalia.GH/Panels/ChatWindow.cs` | `savekey` takes a URL; new `detect`; drop `KeyTargets` |
| `Physalia.UI/src/lib/chat/providers.ts` | 3 new providers; `needsUrl` / `defaultUrl` / `detect` |
| `Physalia.UI/src/lib/chat/Setup.svelte` | two-box form and detect button |
| `Physalia.McpBridge/FileTokenCache.cs` | retrofit onto `ISecretStore` (linked compile) |

---

## 7. Work order

1. **Core store, resolver and tests.** Pure and testable in `Physalia.Core.Tests`, no Rhino needed.
   Includes the different-account decrypt failure as a named result, not an exception.
2. **`ModelApi` object plus the component and param renames**, once the Option A/B question is answered.
3. **UI**: `providers.ts`, the two-box form, the detect button, the bridge verbs.
4. **Migration** off the YAML.
5. **Bridge retrofit** onto the shared store.

## 8. What cannot be verified from the command line

- The DPAPI round-trip **inside Rhino**, under the Rhino process's own identity.
- Saved-file restore: open a pre-change `.gh` and a shipped preset after step 2 and confirm the model
  nodes still solve with their wires intact. This is the step most likely to fail quietly.
- The Mac path — Keychain is unwritten, and `ISecretStore` is the seam where it lands.
- Any UI change needs `dotnet build src/Physalia.slnx -c Debug`, or the Svelte edit stays stranded in
  `dist/` and Rhino loads the old bundle.


---

## 9. What was actually built (2026-09-04)

**Core — `Physalia.Core/Config/`**

- `Secrets/ISecretStore.cs` — the one seam. `Read()` returns a `SecretReadResult` whose status is
  `Ok` / `Empty` / **`Unreadable`**; that third case is the whole reason the type exists rather than
  a nullable string. `Write`, `Delete`, `Description`, `IsEncrypted`.
- `Secrets/WindowsDataProtection.cs` — DPAPI by direct P/Invoke to `crypt32`, ~40 lines, **no package
  reference**. Byte-identical to `ProtectedData.Protect` (no entropy, `CRYPTPROTECT_UI_FORBIDDEN`,
  current-user scope), which is what lets the MCP bridge move onto it without orphaning the token
  caches it has already written. Throws `CryptographicException`, not `Win32Exception`, because that
  is what both call sites already filter on.
- `Secrets/DpapiSecretStore.cs`, `Secrets/FileSecretStore.cs` (plaintext + `chmod 600`),
  `Secrets/SecretStores.cs` (the per-platform choice, and the only place it is made).
- `ModelApi.cs`, `ProviderCatalog.cs`, `CredentialStore.cs`, `ModelApiResolver.cs`,
  `CredentialMigration.cs`. `Api.cs`'s record is now `LegacyApiKey`, read-only in practice.

**GH** — `GH_ModelApi` / `Param_ModelApi`; `ModelApiComponent` (replacing `ApiKeys`, icon resource
renamed with it); `ModelComponentBase.CreateConfig(modelId, ModelApi)`; `OpenAICompatibleModel`
rebuilt; `WebToolKeys` and `ProviderAvailability` routed through `PhyCredentials`, the process-wide
store + resolver; `ChatWindow` gained `detect` and taught `savekey` about URLs, and lost both mapping
tables.

**UI** — three new providers, `needsUrl`/`defaultUrl`/`detect` on `Provider`, a two-field form and a
Detect button in `Setup.svelte`. The composer's API-key capture mode is **deleted** — `apiKeyProvider`,
`onsavekey`, the placeholder branch and the clear-on-mode-change effect all went with it.

**Bridge** — `FileTokenCache` now calls `WindowsDataProtection` through a **linked compile** of the
Core file, and `System.Security.Cryptography.ProtectedData` is gone from its `.csproj`.

**Tests** — 20 new in `Physalia.Core.Tests/Config/` over a `FakeSecretStore`; 669 pass.

### Two things the build itself taught

1. **The resolver had to take an injected environment lookup.** The first version read
   `Environment.GetEnvironmentVariable` directly, and a test asserting "a Tavily key alone configures
   no LLM provider" failed on a machine that happened to have `OPENAI_API_KEY` exported. The
   resolution order is the most important behaviour here and it was, until that point, untestable.
2. **`GH_ApiKey` was never the hard part — the icon was.** `PhyBase` resolves an icon from the
   concrete type name (`Physalia.GH.Resources.<TypeName>.png`) and falls back to the generic brain,
   silently. Renaming the class without renaming the resource loses the icon with no warning at
   build time.

### What this cost

- **`Files/PRESETS/Physalia/Local LLM - Python 3.gh` almost certainly needs re-saving.**
  `LlamaCppModelInfo`'s input is described as "An OpenAI Compatible Model whose Base URL points at
  your llama-server", so that preset holds an `OpenAICompatibleModel`, whose ComponentGuid just
  changed. It will load with a placeholder. The archives are compressed, so this could not be
  confirmed by inspection — open the preset in Rhino, rewire the Model API node, re-save. The six
  Claude Code / Codex presets use CLI model nodes and should be untouched.
- **Migration copies, it does not delete.** Keys already in `API_KEY_CONFIG.YAML` are imported into
  the encrypted store once, and `CredentialImport.PlaintextRemains` reports that a plain-text copy is
  still in the file — but nothing blanks it. Erasing values out of a file the user hand-wrote is not
  reversible and is not ours to do unasked. A "remove the plain-text copies" affordance on the setup
  page is the natural follow-up, and is **not built**.
- **macOS still has no Keychain store.** `SecretStores.For` falls through to `FileSecretStore`
  (plaintext, owner-only mode) off Windows, and says so honestly via `IsEncrypted`. The seam is in
  place: one new class plus one line in `SecretStores.For`.


---

## 10. The legacy YAML path was removed (same day)

`Files/API_KEY_CONFIG.YAML` began as the demoted third resolution source. It is now **gone**, along
with `Api.cs` (the parser and `SetKey`), `LegacyApiKey`, `CredentialMigration`, the
`CredentialStore.LegacyImported` marker, `ProviderInfo.LegacyKeyName` / `IdForLegacyName`,
`PhyCredentials.LegacyConfigPath` / `EnsureMigrated`, the `.example` template, and the `.gitignore`
entry.

**Why, on the same reasoning as Option B.** There is no released version to migrate from, so the file
was not a compatibility obligation — it was a second way to configure the same thing, and two of
those disagree eventually. It also could not express the thing this whole rework is about: the YAML
had **nowhere to put an endpoint**, so every provider needing a non-default host (Alibaba's regions,
a Z.AI Coding Plan key, a private gateway) had to be set up in the chat window regardless.

Resolution is now exactly two sources, environment variable then encrypted store.

**What was actually given up:** the YAML's `env_vars` block let a user point a provider at a
*custom-named* environment variable. Only the conventional names in `ProviderCatalog.EnvVars` are
consulted now (`ANTHROPIC_API_KEY`, `GEMINI_API_KEY`/`GOOGLE_API_KEY`, `DASHSCOPE_API_KEY`, …). If
someone ever needs an arbitrary variable name, it belongs as a field on the store entry, not as a
returning config file.

**Tests:** `CredentialMigrationTests` deleted, the two YAML-fallback cases dropped from
`ModelApiResolverTests`, the legacy-marker case dropped from `CredentialStoreTests`. 659 pass.


---

## 11. Availability vs consent — the activation list (2026-09-04)

A third file joined the design: `%LOCALAPPDATA%/Physalia/providers.json`, plain JSON, listing which
providers the user has actually **connected**.

**The problem it fixes.** Resolution treated any resolvable credential as configuration. So a
developer box with `GEMINI_API_KEY` exported for something else, or the Claude Code CLI installed for
ordinary terminal work, arrived at first run already "configured" for providers nobody had chosen —
and the pipeline would spend that quota. Detection is evidence a provider *could* be used; it is not
a decision that it *should* be.

**Shape.** `ProviderActivation` (Core) is the list; `ProviderStatus(Id, Activated, Source, Detail)`
is what the setup page renders from, where `Source` is `None` / `Environment` / `Stored` / `Detected`
and `Detail` names the environment variable a key came from. `ModelApiResolver.Resolve` is now gated
on activation; `StatusFor` / `Statuses` are the un-gated view. `ProviderAvailability.StatusesAsync`
composes the probed half (Core cannot see a PATH), and `ConfiguredProviderIdsAsync` is derived from
it as "Ready" = available AND activated.

**Not in the encrypted store, on purpose.** It holds no secrets, so keeping it plain means the user
can read it — and, more usefully, it survives a `credentials.dat` that cannot be decrypted, so the
window can still say *which* providers were connected while asking for the keys again.

**Two rules that fell out of it.**
- **Saving a typed key activates it.** Typing a key and pressing Save IS the opt-in; asking twice
  would be ceremony. Only a credential Physalia *found* on its own needs the extra button.
- **Disconnect forgets the stored key**, rather than merely clearing the flag. Deactivating while
  keeping the secret would leave a credential on disk that nothing uses and nothing displays. This
  also closes the gap §9 left open: there is now a way to remove a stored key from the UI.

**Bridge verbs:** `connect?provider=`, `disconnect?provider=`. `detect` still exists but no longer
implies configuration — it reports "found. Press Connect to use it in Physalia."

**The guide page goes quiet once a provider is available.** The blurb, numbered install steps,
install commands and console links all exist to get someone TO the point of having a provider — in
front of someone already past it they are noise around the one control that matters. So an available
provider shows its name, ONE line saying what was found ("Claude Code found on your machine.", "An
Anthropic key was found in your local environment (ANTHROPIC_API_KEY)."), and the footer. The
variable name moved out of the button and into that line, so the button is a plain verb again.
*Consequence worth knowing:* a provider whose key lives in an environment variable can no longer
reach its console links from this page, since it never renders the un-available branch.

Tests: `ProviderActivationTests` (10), including the headline case — an environment key alone
resolves to null, is reported as available with its variable name, and resolves only once connected.
