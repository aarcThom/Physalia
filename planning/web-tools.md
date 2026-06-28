# Web Search & Web Fetch Tools — Research & Implementation

> 2026-06-27. Two new LLM tools for Physalia: **`web_search`** (Tavily — needs a key) and **`read_url`**
> (Jina Reader — keyless). **Implemented & builds clean; live-Rhino test pending.** Both are
> `ToolComponentBase` subclasses reusing a shared `HttpClient` and `HttpErrorMapper`.
>
> **Implemented:** `Physalia.Core/Web/WebTools.cs` (`SearchTavilyAsync`, `FetchUrlAsync` — pure HTTP,
> `ConfigureAwait(false)`); GH tools `WebSearch.cs` (`web_search`) + `ReadUrl.cs` (`read_url`) in
> `Components/Tools/`; `WebToolKeys.cs` resolves keys from the `web_search` YAML section. The
> `web_search` section was added to `API_KEY_CONFIG.YAML.example`. **Provider chosen: Tavily.**
>
> **Async tools:** `ToolComponentBase` now supports asynchronous tools — set `RunsAsync => true` and
> implement `ExecuteCallAsync(call, ct)`; the base runs the call batch off the solve thread and latches
> the result signal on a self-scheduled solve (the Reasoner async pattern: one dispatched signal at a
> time, queued signals wait). So the web tools **no longer block the GH solve thread** on the network
> (each applies its own 20s/30s timeout via a linked CTS). Synchronous tools (ComponentSearch) keep
> `ExecuteCall` and behave exactly as before.

## Headlines

- **Bing Web Search API is dead** — decommissioned 11 Aug 2025 (replaced only by "Grounding with Bing"
  *inside* Azure AI Agents, not a callable REST endpoint). Excluded. This also weakens any provider that
  resold Bing — prefer an independent index.
- **Every agent-grade search API needs a key.** The keyless "options" (DuckDuckGo Instant Answer, HTML
  scraping) are not real web search. So yes — a key is required (you were right).
- **Web fetch can stay keyless** via Jina Reader (`r.jina.ai`).
- **Google Programmable Search (CSE)** is closed to new customers and fully discontinued 2027-01-01 — do
  not adopt.

## Search provider comparison (June 2026)

| Provider | Key | Free tier | Paid /1k | Output | .NET call | Notes |
|---|---|---|---|---|---|---|
| **Tavily** ⭐ | yes (`tvly-…`) | **1,000 credits/mo, no card** | $8 basic / $16 advanced | LLM-clean `content` per result + optional synthesized `answer` | `POST`, `Authorization: Bearer`, JSON | Agent-shaped; same key drives `/extract` (fetch). |
| **Brave** ⭐ fallback | yes | $5/mo credit ≈ 1k q (**card required**, uncapped overage) | $5 (AI tiers $3–9) | raw SERP (`web.results[]` + `extra_snippets`); **independent index** | `GET`, `X-Subscription-Token` header | Low latency; not a Bing/Google reseller. |
| Serper.dev | yes | 2,500 one-time | ~$1 → $0.30 at scale | raw Google SERP | `POST`, `X-API-KEY` | Cheapest Google data; free is one-time; reseller ToS. |
| Exa | yes | 1,000/mo + $10 | $5–7 | neural search + page contents | `POST`, `x-api-key` | Great for "find pages about X"; keyword recall trails Google. |
| You.com | yes | $100 signup credit | $5 (content bundled) | search + full page content | `x-api-key`, JSON | Generous trial; content-with-search is handy. |
| Linkup | yes | ~1–4k/mo (€5) | €5 | sourced answer + raw | JSON | EU vendor; figures vary. |
| SerpAPI | yes | 100–250/mo | $25 | rich multi-engine SERP | `GET` | Most expensive; overkill. |
| Jina `s.jina.ai` | optional | 10M tokens/key (keyless = rate-limited) | token-metered | LLM-clean top-5 text+URLs | `GET` ±Bearer | Doubles as fetch; token billing. |
| ~~Bing~~ | — | — | — | — | — | **Retired 2025-08-11.** |
| ~~Google CSE~~ | — | 100/day | $5 | raw | `GET` | **Closed to new; ends 2027-01-01.** |
| DuckDuckGo IA | no | — | — | **not web search** (instant answers only) | `GET` | Reject. |

### Top-two concrete calls

**Tavily** (recommended primary):
```
POST https://api.tavily.com/search
Authorization: Bearer tvly-YOUR_KEY
{ "query": "Grasshopper Kangaroo solver", "max_results": 5, "include_answer": "basic" }
→ { "answer": "...", "results": [ { "title", "url", "content", "score" } ], "usage": { "credits": 1 } }
```
**Brave** (recommended fallback):
```
GET https://api.search.brave.com/res/v1/web/search?q=grasshopper+kangaroo+solver&count=5
X-Subscription-Token: YOUR_KEY
→ { "web": { "results": [ { "title", "url", "description", "extra_snippets" } ] } }
```

## Web Fetch (read_url) companion

| Option | Key | How | Verdict |
|---|---|---|---|
| **Jina Reader `r.jina.ai`** ⭐ | no (optional) | `GET https://r.jina.ai/<url>`; optional `x-respond-with: markdown` | **Default.** Zero-config, returns readability-cleaned markdown, handles JS. |
| Tavily Extract | yes (same key) | `POST /extract {urls:[…]}` | Keyed upgrade if Tavily adopted; 1 credit / 5 URLs. |
| HttpClient + local HTML→md | no | fetch + readability/markdown lib | Adds a dependency (AngleSharp/ReverseMarkdown), worse on JS pages. Last resort. |
| Firecrawl / Exa contents / Linkup fetch | yes | scrape endpoints | Only if that vendor is already the search provider. |

**Recommendation:** ship `read_url` **keyless on Jina Reader**, cap output with a `max_chars` truncation.
Optionally route through Tavily Extract later behind the same search key.

## Recommendation for Physalia

**Primary search: Tavily. Fallback: Brave. Fetch: Jina Reader (keyless).** Minimum to ship: **one key
(Tavily)** — the only top option with a genuinely recurring free tier and **no credit card**, agent-shaped
output, the cleanest `POST`+Bearer .NET call, and one key that also powers fetch. Brave is the raw-SERP,
independent-index fallback (but it now requires a card with uncapped overage — surface that to the user).

### Key plug-in to `API_KEY_CONFIG.YAML`

`Api.GetKeys` treats each top-level node as a section with `env_vars:` (leaf→envvar) and `api_keys:`
(leaf→value); env wins; a leaf named `api_key` resolves to the section name, any other leaf to the leaf
name. Add a `web_search` section, one leaf per provider:

```yaml
web_search:
  env_vars:
    tavily: TAVILY_API_KEY
    brave:  BRAVE_SEARCH_API_KEY
    jina:   JINA_API_KEY        # optional — read_url works keyless
  api_keys:
    tavily: ""
    brave:  ""
    jina:   ""
```
Lookup returns `ApiKey("tavily"|"brave"|"jina", …)`. Env-var convention matches the LLM keys
(`TAVILY_API_KEY`, `BRAVE_SEARCH_API_KEY`, `JINA_API_KEY`). Keys never serialize into `.ghjson`
(same posture as model keys). The chat-window paste flow already supports
`Api.SetKey(path, "web_search", "tavily", value)`.

### Tool definitions

**`web_search`** — input `{ query: string (required), count: integer 1-10 default 5 }`. Returns a terse
numbered block: synthesized `answer` first if present, then `N. title — url` + a one-line snippet
(Tavily `results[].content` / Brave `web.results[].description`).

**`read_url`** — input `{ url: string (required), max_chars: integer default 8000 }`. Returns the page as
clean markdown from `r.jina.ai/<url>`, truncated with a `…[truncated]` marker; on failure
`ToolCallResult.Error("Could not fetch <url>: <reason>")`.

### Integration notes
- Reuse the shared `HttpClient` (don't instantiate per call); map non-200 via `HttpErrorMapper.MapStatusCode`.
- Resolve the key once in `OnSolveTick`; if none, return `ToolCallResult.Error("No Tavily key configured —
  add web_search.tavily to API_KEY_CONFIG.YAML")` rather than throwing.
- No extra GH inputs (unlike `ComponentSearch`'s catalog) — `RegisterAdditionalInputs` stays empty;
  `ExecuteCall` parses `call.InputJson`.

## Open decision
- **Search provider:** Tavily (free, no card — recommended) vs Brave (raw SERP, independent index, but
  card required). Could also support both behind the `web_search` key section and pick by which key is set.

## Sources
Bing retirement (learn.microsoft.com/lifecycle/announcements/bing-search-api-retirement); Tavily docs +
pricing (docs.tavily.com, tavily.com/pricing); Brave API docs + pricing (api-dashboard.search.brave.com);
Serper (serper.dev); Exa (exa.ai/pricing); You.com (you.com/docs/search); Linkup (docs.linkup.so);
SerpAPI (serpapi.com/pricing); Jina Reader (jina.ai/reader, github.com/jina-ai/reader); Google CSE
(developers.google.com/custom-search); Firecrawl (firecrawl.dev/pricing).
