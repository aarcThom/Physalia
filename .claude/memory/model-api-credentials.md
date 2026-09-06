---
name: model-api-credentials
description: "2026-09-04/09-06 — credentials in an encrypted per-user store, providers set up (and now edited or switched off) in the chat window, endpoint+key on one GH_ModelApi wire."
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

---

**The first-run setup screen had two separate bugs, both fixed 2026-09-05** (reported as "it only
appears when I move the scroll bar"):

1. **Layout.** The 120px logo plus gaps pushed the welcome text and every provider button below the
   fold at the window's real 460x620 default. The mark now shrinks below 760px of height and
   disappears below 560px. Measured, not guessed — see [[headless-chat-ui-testing]].
2. **State timing.** `needsSetup` stayed false until the async probe returned, so the window claimed
   everything was fine for as long as two PATH scans and a socket timeout took.
   `ProviderAvailability.ConfiguredProviderIdsNow()` answers the credential half synchronously on
   the first tick; a CONNECTED probe-based provider is assumed present until the probe corrects it,
   so someone with Claude Code set up gets no flash of setup.

**Also 2026-09-05: `MCP_SERVERS.YAML` was deleted by the same argument** — servers now live in
`%LOCALAPPDATA%/Physalia/mcp-servers.json`. See [[mcp-setup-page]]. The three per-user stores are
now `credentials.dat` (encrypted), `providers.json` (plain, the opt-in list) and `mcp-servers.json`
(plain, the standard `mcpServers` block).

---

**2026-09-06: a configured provider was a DEAD END, and the fix is three small contracts.**
The picker drew configured providers as non-clickable `<span>` pills, so the connected footer —
written, tested, correct — could not be reached by anyone. A rotated key could not be pasted, a
moved endpoint could not be corrected, and no connection could be switched off, Claude Code
included. Symptom to recognise elsewhere: *a state's UI exists and no route into that state does.*

- **The pill is a door** (`Pill onclick` + a pencil icon). One-line change, and it is what makes
  everything below reachable.
- **A blank key box means KEEP, never CLEAR.** `ProviderStatus` grew `BaseUrl` + `HasStoredKey`;
  the KEY is still never pushed to the page, so `HandleSaveProvider` merges against
  `Store.Get(id)` — otherwise an endpoint-only edit saves an empty key over a live credential.
  Identical contract to the API endpoints page; copy it for any future "edit a stored secret" form.
- **`BaseUrl` is the endpoint IN EFFECT, not the catalog default.** Prefilling the default would
  offer the wrong host back for saving to anyone on an Alibaba region or a Z.AI Coding Plan URL —
  and it would look entirely plausible.
- **Disconnect asks twice only when a key is really on disk.** `HasStoredKey` is the store
  specifically, NOT `source === 'environment'`: an env key is not ours to delete, so that case says
  so and goes straight through, as does a probed CLI. A confirmation nobody needs is one everybody
  clicks through.

Verified headless (`tools/uitest/test_provider_edit.py`, see [[headless-chat-ui-testing]]): pills are
buttons, the URL box prefills the moved endpoint, the key box is blank with a keep-it placeholder,
the install guide is gone, Disconnect opens a confirm for a stored key and Claude Code's page offers
Disconnect with no form at all. Five Core tests cover the new status fields. **Not run in Rhino.**
