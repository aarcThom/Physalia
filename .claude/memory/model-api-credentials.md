---
name: model-api-credentials
description: "2026-09-04 — credentials moved to an encrypted per-user store, providers are set up in the chat window, and endpoint+key collapsed into one GH_ModelApi wire."
metadata: 
  node_type: memory
  type: project
  originSessionId: 5a14cfb4-98bd-4bf8-94b0-58f4ef00f6af
  modified: 2026-09-04T23:35:42.066Z
---

**BUILT 2026-09-04, not yet run in Rhino.** Plan + build record: `planning/model-api-credentials.md`.
CLAUDE.md's "Credentials" section is current.

**The argument that decided it.** Encrypting `API_KEY_CONFIG.YAML` would have been *wrong* — being
hand-editable was its whole purpose. It became right only once the chat window took over authoring:
nobody edits an opaque store, so nothing is lost. That is the general rule — *encrypt what the
machine writes, not what the human writes.* The MCP OAuth token cache was always the first kind.

**Exactly TWO sources** in `ModelApiResolver`: env var → encrypted store. No credential on disk beats
any at-rest encryption, so env stays first. Endpoint and key resolve **independently**, so a
shell-managed token still picks up a custom endpoint.

**`API_KEY_CONFIG.YAML` is DELETED** (2026-09-04, same day it was demoted) — parser, `.example`,
one-time importer, gitignore entry, the lot. With no released version to migrate from it was a second
way to configure the same thing, and it could not express an endpoint at all, which is the whole
point of the rework. **Don't reintroduce a config file.** What was given up: the `env_vars` block let
a user name a CUSTOM environment variable; only the conventional names in `ProviderCatalog.EnvVars`
are consulted now.

**Availability is NOT consent** (added 2026-09-04). `ProviderActivation` —
`%LOCALAPPDATA%/Physalia/providers.json`, **plain JSON, deliberately unencrypted** (no secrets; stays
readable; survives an undecryptable credentials.dat so the window can still say WHICH providers were
connected) — lists what the user actually connected. `Resolve` returns null for anything not on it;
`StatusFor` is the un-gated view so the page can OFFER a found key. Before this, a dev box with
`GEMINI_API_KEY` exported for something else, or a Claude Code CLI installed for terminal work,
arrived at first run already "configured" for providers nobody chose — and the pipeline would spend
that quota. Two rules that fell out: **saving a typed key activates it** (typing IS the opt-in),
and **Disconnect FORGETS the stored key** (a deactivated-but-kept secret is one nothing uses and
nothing shows — this is also the remove-my-key affordance).

**The setup guide goes quiet when a provider is already available** — blurb, numbered steps, install
commands and console links are all suppressed, leaving the name, ONE found-line, and the footer.
Those exist to get someone TO a working provider; in front of someone past it they bury the control.

**What it actually buys** is honest and limited: it stops another local account, and keeps plaintext
out of backups/screenshots/support bundles. It does NOT stop code running as the user — that calls
`CryptUnprotectData` exactly as we do.

Traps worth keeping:

- **`Unreadable` ≠ `Empty`.** A store written by another Windows account must not report "nothing
  configured" — that sends the user to re-enter keys sitting right there. Saving over an unreadable
  store is refused, or it discards every other provider its real owner had.
- **`ISecretStore` is the ONLY platform seam** — macOS Keychain is one class + one line in
  `SecretStores.For`. DPAPI is our own ~40-line P/Invoke, byte-compatible with `ProtectedData`, and
  the MCP bridge shares it by **linked compile** (leaf net8.0 exe, no ProjectReference to Core). One
  DPAPI implementation in the repo, and one package reference dropped.
- **`PhyBase` resolves icons by CONCRETE TYPE NAME** and falls back to the generic brain **silently**.
  Renaming a component class loses its icon with no build warning — rename `Resources/<Type>.png` too.
- **Injected environment lookup**, or the resolution order is untestable: a dev box with
  `OPENAI_API_KEY` set failed a test about Tavily. See [[design-fork-then-build-through]].

**Option B was taken** (no user `.gh` files existed): `OpenAICompatibleModel` dropped `Base URL` and
took a NEW ComponentGuid, because reusing it would restore a 4-param archive onto a 3-param component
and land the Picker on Max Tokens. **Cost: `Files/PRESETS/Physalia/Local LLM - Python 3.gh` needs
re-saving in Rhino** — `LlamaCppModelInfo` takes "an OpenAI Compatible Model", so it holds one.
The `.gh` archives are compressed, so this could not be confirmed by inspection.

New providers: Alibaba (`dashscope-intl…/compatible-mode/v1`, REGIONAL), Z.AI
(`api.z.ai/api/paas/v4` — **a Coding Plan key needs `/api/coding/paas/v4`, not interchangeable**),
Moonshot (`api.moonshot.ai/v1`, `.cn` is a separate account). All ride `OpenAICompatibleProvider`.

Setup page: probed providers (Claude Code, Codex, local llama.cpp) get ONE **Detect** button and
store **nothing** — a stored flag would keep claiming a CLI exists after uninstall. Everything else
gets **API URL** + **API key** boxes. The composer's API-key capture mode is deleted.
See [[mcp-setup-page]] for the sibling page.
