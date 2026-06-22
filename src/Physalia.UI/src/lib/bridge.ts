// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

// The contract between the Svelte chat UI and its Eto WebView host
// (Physalia.GH/Panels/ChatWindow.cs).
//
//   C# -> JS : the host calls window.physalia.{setHistory,setStream,setState}
//   JS -> C# : the app stashes the outgoing message as JSON on window
//              (__physaliaPending) and navigates to `phbridge://submit`.
//              The host cancels that navigation, then pulls the JSON back
//              with __physaliaTake(). A custom-URI navigation + a pull-back
//              read is used (instead of putting data in the URL) because an
//              image-bearing message is far larger than a URL can carry.

/** A committed image already in the conversation, base64-encoded for display. */
export interface UiImage {
	base64: string;
	mediaType: string;
}

/** A tool call surfaced from ToolCallContent / ToolResultContent in the conversation. */
export interface UiTool {
	id: string;
	name: string;
	/** Maps to the ai-elements Tool component state. */
	state: 'input-streaming' | 'input-available' | 'output-available' | 'output-error';
	/** Parsed tool input (object) when the JSON is valid, else the raw string. */
	input?: unknown;
	/** Tool result text (present once the tool has run). */
	output?: string;
	/** Error text when the tool failed. */
	errorText?: string;
}

/** One committed conversation turn, as pushed by the host. */
export interface UiMessage {
	id: string;
	role: 'user' | 'assistant';
	/** Raw turn text; assistant text may contain <think>...</think> reasoning. */
	text: string;
	images?: UiImage[];
	tools?: UiTool[];
}

/** Connection / busy state of the wired pipeline. */
export interface UiState {
	connected: boolean;
	busy: boolean;
	/** No LLM provider is configured yet — show the first-run setup screen. */
	needsSetup: boolean;
	status: string;
	/** Setup-screen ids of every provider already configured (matches providers.ts ids). */
	configuredProviders: string[];
}

/** Outcome of a save-API-key request, pushed back by the host after it writes the config. */
export interface SetupResult {
	/** Provider id the result is for (matches a providers.ts id). */
	provider: string;
	/** True when the key was saved successfully. */
	ok: boolean;
	/** Human-readable message to show on the setup page. */
	message: string;
}

/** Functions the host invokes on the page (set by the app on mount). */
export interface PhysaliaHost {
	setHistory(messages: UiMessage[]): void;
	setStream(text: string | null): void;
	setState(state: UiState): void;
	setSetupResult(result: SetupResult | null): void;
}

/** An image attached in the prompt box, ready to send to the host. */
export interface SubmitImage {
	/** base64 payload only (no data: prefix); the host decodes it to bytes. */
	base64: string;
	mediaType: string;
	filename: string;
}

/** The outgoing user message handed to the host. */
export interface SubmitMessage {
	text: string;
	images: SubmitImage[];
}

declare global {
	interface Window {
		physalia?: PhysaliaHost;
		__physaliaPending?: string;
		__physaliaTake?: () => string;
	}
}

const THINK_BLOCK = /<think(?:ing)?>([\s\S]*?)<\/think(?:ing)?>/gi;
const OPEN_THINK = /<think(?:ing)?>([\s\S]*)$/i;

/**
 * Splits assistant text into reasoning (the contents of <think>/<thinking>
 * blocks, concatenated) and the visible body (everything else). An unclosed
 * trailing <think> — which happens while reasoning is still streaming — is
 * treated as all-reasoning so the chain-of-thought fills in live.
 */
export function splitThinking(text: string): { reasoning: string; body: string } {
	if (!text) {
		return { reasoning: '', body: '' };
	}

	const reasoningParts: string[] = [];
	let body = text.replace(THINK_BLOCK, (_m, inner: string) => {
		reasoningParts.push(inner.trim());
		return '';
	});

	const open = body.match(OPEN_THINK);
	if (open) {
		reasoningParts.push(open[1].trim());
		body = body.slice(0, open.index).trimEnd();
	}

	return { reasoning: reasoningParts.join('\n\n').trim(), body: body.trim() };
}

/** Strips a `data:<mime>;base64,` prefix, returning the raw base64 payload. */
export function stripDataUrl(url: string): string {
	const comma = url.indexOf(',');
	return comma >= 0 && url.startsWith('data:') ? url.slice(comma + 1) : url;
}

/** A piece of an assistant turn: prose markdown, or a JSON blob to collapse. */
export type ContentSegment = { kind: 'text'; text: string } | { kind: 'json'; json: string };

// Bare JSON shorter than this stays inline (so small `{"x":1}` mentions aren't collapsed).
const MIN_BARE_JSON = 120;

/**
 * Splits assistant text into prose and JSON segments. JSON is collapsed in the UI because
 * graph dumps (ghjson) flood the chat. Catches:
 *   - fenced ```json blocks (and unlabelled fences whose body parses as JSON),
 *   - large bare JSON objects/arrays embedded in prose,
 *   - a large, still-unclosed bare JSON tail (so it collapses *while* streaming).
 * Other fenced code (```python etc.) is left in the prose for normal markdown rendering.
 */
export function splitContent(text: string): ContentSegment[] {
	const segments: ContentSegment[] = [];
	if (!text) {
		return segments;
	}

	let proseStart = 0;
	let i = 0;
	const n = text.length;

	const flushProse = (end: number) => {
		if (end > proseStart) {
			segments.push({ kind: 'text', text: text.slice(proseStart, end) });
		}
	};

	while (i < n) {
		// Fenced code block.
		if (text.startsWith('```', i)) {
			const headerEnd = text.indexOf('\n', i + 3);
			const close = headerEnd === -1 ? -1 : text.indexOf('```', headerEnd + 1);
			if (headerEnd !== -1) {
				const lang = text.slice(i + 3, headerEnd).trim().toLowerCase();
				if (close !== -1) {
					const body = text.slice(headerEnd + 1, close).trim();
					if (lang === 'json' || (lang === '' && isJson(body))) {
						flushProse(i);
						segments.push({ kind: 'json', json: prettyJson(body) });
						i = proseStart = close + 3;
						continue;
					}

					// Non-JSON fence: keep it in the prose, just skip past it.
					i = close + 3;
					continue;
				}

				// Streaming: a ```json fence has opened but its closing fence hasn't
				// arrived yet. Collapse the partial body into the JSON pill now (raw,
				// since it won't parse until complete) so it doesn't flash a fenced
				// code block above the pill until streaming finishes.
				if (lang === 'json') {
					flushProse(i);
					segments.push({ kind: 'json', json: text.slice(headerEnd + 1).trim() });
					i = proseStart = n;
					continue;
				}
			}
		}

		// Bare JSON object/array.
		const ch = text[i];
		if ((ch === '{' || ch === '[') && looksLikeJsonStart(text, i)) {
			const end = matchBalanced(text, i);
			if (end !== -1) {
				const candidate = text.slice(i, end + 1);
				if (candidate.length >= MIN_BARE_JSON && isJson(candidate)) {
					flushProse(i);
					segments.push({ kind: 'json', json: prettyJson(candidate) });
					i = proseStart = end + 1;
					continue;
				}
			} else if (n - i >= MIN_BARE_JSON) {
				// Unclosed and large — a JSON tail mid-stream. Collapse the remainder.
				flushProse(i);
				segments.push({ kind: 'json', json: text.slice(i) });
				i = proseStart = n;
				continue;
			}
		}

		i++;
	}

	flushProse(n);
	return segments;
}

// Index of the bracket matching the one at `start` (string-aware), or -1 if unclosed.
function matchBalanced(text: string, start: number): number {
	const open = text[start];
	const close = open === '{' ? '}' : ']';
	let depth = 0;
	let inStr = false;
	let esc = false;

	for (let i = start; i < text.length; i++) {
		const c = text[i];
		if (inStr) {
			if (esc) {
				esc = false;
			} else if (c === '\\') {
				esc = true;
			} else if (c === '"') {
				inStr = false;
			}
			continue;
		}

		if (c === '"') {
			inStr = true;
		} else if (c === open) {
			depth++;
		} else if (c === close) {
			depth--;
			if (depth === 0) {
				return i;
			}
		}
	}

	return -1;
}

// Cheap guard so a stray `{` in prose isn't treated as JSON: the next non-space char
// must plausibly begin a JSON object/array.
function looksLikeJsonStart(text: string, i: number): boolean {
	let j = i + 1;
	while (j < text.length && /\s/.test(text[j])) {
		j++;
	}

	const c = text[j];
	if (c === undefined) {
		return false;
	}

	if (text[i] === '{') {
		return c === '"' || c === '}';
	}

	return c === '{' || c === '[' || c === '"' || c === ']' || c === '-' || /[0-9tfn]/.test(c);
}

function isJson(s: string): boolean {
	if (!s) {
		return false;
	}

	try {
		JSON.parse(s);
		return true;
	} catch {
		return false;
	}
}

function prettyJson(s: string): string {
	try {
		return JSON.stringify(JSON.parse(s), null, 2);
	} catch {
		return s;
	}
}
