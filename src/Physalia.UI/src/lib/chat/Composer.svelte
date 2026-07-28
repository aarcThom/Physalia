<script lang="ts">
	// The prompt box. A contenteditable editor (not a textarea) so Physalia "/" commands can render in
	// monospace on a blue chip while the surrounding prose stays sans — the caret lives inside the
	// styled content, so a per-token font never drifts it (a transparent-textarea-over-backdrop scheme
	// cannot do per-token fonts without the caret drifting). `text` is the plain source of truth; the
	// editor DOM is rendered from it imperatively (highlighted spans) with the caret restored by
	// character offset after each change. Newlines are `\n` in `text` and `<br>` in the DOM.
	//
	// Image intake (file picker, clipboard paste, drag-and-drop) is handled here: converted to base64
	// via FileReader (no crypto.randomUUID / blob URLs, which fail in the file:// WebView). Each image
	// inserts a Claude-Code-style "[image#N]" token at the caret and a thumbnail in the strip above; the
	// tokens are composer-only scaffolding, stripped before send (images travel as real image blocks).
	import { onMount, tick } from 'svelte';
	import XIcon from '@lucide/svelte/icons/x';
	import LayersIcon from '@lucide/svelte/icons/layers';
	import BoxIcon from '@lucide/svelte/icons/box';
	import WrenchIcon from '@lucide/svelte/icons/wrench';
	import ShapesIcon from '@lucide/svelte/icons/shapes';
	import BrainIcon from '@lucide/svelte/icons/brain';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import { stripDataUrl, type ComponentTabInfo, type SubmitMessage } from '$lib/bridge';

	interface Props {
		/** No Conversation Log wired — shown as a hint, but the box stays usable (sending
		 *  still mints a Prompt Signal). */
		disconnected: boolean;
		/** Pipeline is mid-run — block input until it settles, to avoid re-submitting. */
		busy: boolean;
		/** Setup not finished (no provider configured) — block input; there's nothing to send to. */
		disabled?: boolean;
		/** Supplementary instruction text from the host (hook up a Conversation Log, add an LLM
		 *  Call, …). Shown as this box's placeholder — the window has no separate status row. */
		status?: string;
		/** When set, the box captures an API key for this provider instead of sending a message. */
		apiKeyProvider?: { id: string; label: string } | null;
		/** True when an Add Image human tool is wired — without it, image intake (paste, drag-drop,
		 *  file picker) is fully disabled and prompts are text-only. */
		imageToolWired?: boolean;
		/** True when a Geometry Snapshot human tool is wired in attach mode ("Send With Default
		 *  Message" unchecked) — it grants its own image lane (addSnapshot), independent of the Add
		 *  Image tool, which still gates paste/drop/picker. */
		snapshotAttachWired?: boolean;
		/** Names of clusters the model may use, for the "/cl/" reference autocomplete. */
		clusterNames?: string[];
		/** Names of tools currently in use, for the "/t/" reference autocomplete. */
		toolNames?: string[];
		/** Grounded components grouped by tab, for the "/c/<tab>/<component>" staged autocomplete. */
		componentTabs?: ComponentTabInfo[];
		onsend: (message: SubmitMessage) => void;
		/** Called with the pasted API key when in apiKeyProvider mode. */
		onsavekey?: (providerId: string, key: string) => void;
	}

	let {
		disconnected,
		busy,
		disabled = false,
		status = '',
		apiKeyProvider = null,
		imageToolWired = false,
		snapshotAttachWired = false,
		clusterNames = [],
		toolNames = [],
		componentTabs = [],
		onsend,
		onsavekey
	}: Props = $props();

	interface PendingImage {
		id: number;
		base64: string;
		mediaType: string;
		filename: string;
		/** Which human tool let this image in — the tool that granted it is the tool that can revoke
		 *  it (see the stale-attachment effect). 'user' = paste/drop/picker (Add Image);
		 *  'snapshot' = the geometry button in attach mode (Geometry Snapshot). */
		source: 'user' | 'snapshot';
	}

	// Block while the pipeline is busy, during setup (no provider yet), or while no Conversation Log is wired —
	// EXCEPT in API-key mode, where the box stays live so the user can paste their key.
	let inert = $derived(busy || ((disabled || disconnected) && !apiKeyProvider));

	let text = $state('');
	let pending = $state<PendingImage[]>([]);
	let editorRef = $state<HTMLDivElement | null>(null);
	let fileInputRef = $state<HTMLInputElement | null>(null);
	let composing = false; // true during IME composition — skip re-canonicalising the DOM
	let nextId = 0;

	// Placeholder priority: API-key capture > blank while the LLM is actively working > setup
	// hint > the host's supplementary instructions (hook up components, …) > the send hint.
	// The host's "Working…" status is deliberately unreachable — busy blanks the box first.
	let placeholder = $derived(
		apiKeyProvider
			? `Paste your ${apiKeyProvider.label} API key here, then press Enter`
			: busy
				? ''
				: disabled
					? 'Finish setup to start chatting…'
					: status
						? status
						: disconnected
							? ''
							: 'Send a message…  (Enter to send, Shift+Enter for a new line)'
	);

	// All [image#N] tokens. Order in the text mirrors insertion order, which mirrors `pending`.
	const TOKEN = /\[image#\d+\]/g;

	// The memory tool is a normal tool, but it uniquely takes a scope: "/t/memory/global" or
	// "/t/memory/local". These are the second-level names offered after "/t/memory/".
	const MEMORY_TOOL = 'memory';
	const MEMORY_SCOPES = ['global', 'local'];

	// Built-in window commands, always offered in the "/" menu (no grounding or tool needed).
	// Unlike the reference kinds they are complete commands — accepting one inserts "/<name>"
	// with no sub-path. The host intercepts a message that is exactly one of these.
	const BUILTIN_COMMANDS = ['export'];
	function hasMemoryTool(): boolean {
		return toolNames.some((n) => n.toLowerCase() === MEMORY_TOOL);
	}

	onMount(() => render());

	// Clear the box whenever API-key mode is entered, left, or switched between providers.
	let prevKeyProviderId = $state<string | null>(null);
	$effect(() => {
		const id = apiKeyProvider?.id ?? null;
		if (id !== prevKeyProviderId) {
			prevKeyProviderId = id;
			text = '';
			pending = [];
			void tick().then(() => render(0));
		}
	});

	// If the tool that admitted an image is unwired mid-composition (component deleted/unwired on the
	// canvas), discard that image and its [image#N] token — nothing image-shaped may outlive the tool
	// that granted it. Each lane is revoked by its own tool: Add Image for pasted/dropped/picked
	// images, Geometry Snapshot (in attach mode) for captured snapshots.
	$effect(() => {
		let stale = new Set<number>();
		pending.forEach((image, i) => {
			if (image.source === 'snapshot' ? !snapshotAttachWired : !imageToolWired) {
				stale.add(i);
			}
		});
		dropImagesAt(stale);
	});

	// Whole-window drag-and-drop. Listeners exist only while the Add Image tool is wired — the
	// reactive dep detaches them live, so an unwired window rejects drops at the browser level.
	$effect(() => {
		if (!imageToolWired) {
			return;
		}
		let onDragOver = (e: DragEvent) => {
			if (e.dataTransfer?.types?.includes('Files')) {
				e.preventDefault();
			}
		};
		let onDrop = (e: DragEvent) => {
			if (e.dataTransfer?.types?.includes('Files')) {
				e.preventDefault();
			}
			if (e.dataTransfer?.files && e.dataTransfer.files.length > 0) {
				void addImages(e.dataTransfer.files);
			}
		};
		document.addEventListener('dragover', onDragOver);
		document.addEventListener('drop', onDrop);
		return () => {
			document.removeEventListener('dragover', onDragOver);
			document.removeEventListener('drop', onDrop);
		};
	});

	function readAsDataUrl(file: File): Promise<string> {
		return new Promise((resolve, reject) => {
			let reader = new FileReader();
			reader.onload = () => resolve(reader.result as string);
			reader.onerror = () => reject(reader.error ?? new Error('Failed to read file.'));
			reader.readAsDataURL(file);
		});
	}

	// ---- Editor rendering + caret ----------------------------------------------------------------

	function escapeHtml(s: string): string {
		return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
	}

	// Plain (non-command) text → HTML: escape, then newlines become <br> (the DOM's line break).
	function renderPlain(s: string): string {
		return escapeHtml(s).replace(/\n/g, '<br>');
	}

	// Builds the editor's innerHTML from `text`: command tokens wrapped in <span class="slash-cmd">
	// (styled monospace-on-chip by app.css), the rest plain. Commands never contain a newline.
	function highlightHtml(value: string): string {
		let out = '';
		let plainStart = 0;
		let i = 0;
		const flushPlain = (upto: number) => {
			if (upto > plainStart) {
				out += renderPlain(value.slice(plainStart, upto));
			}
		};

		while (i < value.length) {
			const atBoundary = i === 0 || /\s/.test(value[i - 1]);
			if (value[i] === '/' && atBoundary) {
				const end = commandEnd(value, i);
				if (end > i + 1) {
					flushPlain(i);
					out += `<span class="slash-cmd">${escapeHtml(value.slice(i, end))}</span>`;
					i = end;
					plainStart = i;
					continue;
				}
			}
			i++;
		}
		flushPlain(value.length);
		return out;
	}

	// The end index (exclusive) of the "/" command starting at i, or i when it is not a command.
	// "/cl/<cluster>", "/c/<tab>/<component>", "/t/<tool>" extend across a matched name; an in-progress
	// or bare "/token" extends across the run of non-whitespace.
	function commandEnd(value: string, i: number): number {
		const four = value.slice(i, i + 4).toLowerCase();
		const three = value.slice(i, i + 3).toLowerCase();

		if (four === '/cl/') {
			const m = matchKnownName(value.slice(i + 4), clusterNames);
			return m !== null ? i + 4 + m.length : runEnd(value, i + 4);
		}
		if (three === '/c/') {
			const slash = slashIndex(value, i + 3);
			if (slash > i + 3) {
				const m = matchKnownName(value.slice(slash + 1), componentNamesFlat());
				if (m !== null) {
					return slash + 1 + m.length;
				}
			}
			return runEnd(value, i + 3);
		}
		if (three === '/t/') {
			const m = matchKnownName(value.slice(i + 3), toolNames);
			if (m !== null) {
				// The memory tool extends across a "/global" or "/local" scope suffix.
				const afterName = i + 3 + m.length;
				if (m.toLowerCase() === MEMORY_TOOL && value[afterName] === '/') {
					const scope = matchKnownName(value.slice(afterName + 1), MEMORY_SCOPES);
					if (scope !== null) return afterName + 1 + scope.length;
				}
				return afterName;
			}
			return runEnd(value, i + 3);
		}
		return runEnd(value, i + 1);
	}

	function runEnd(value: string, from: number): number {
		let e = from;
		while (e < value.length && !/\s/.test(value[e])) e++;
		return e;
	}

	// Index of the next "/" at or after `from`, not crossing a newline; -1 if none.
	function slashIndex(value: string, from: number): number {
		for (let j = from; j < value.length; j++) {
			if (value[j] === '/') return j;
			if (value[j] === '\n') return -1;
		}
		return -1;
	}

	// Longest-first, case-insensitive; requires a non-word boundary after the name.
	function matchKnownName(rest: string, names: string[]): string | null {
		let sorted = names.filter(Boolean).slice().sort((a, b) => b.length - a.length);
		let lower = rest.toLowerCase();
		for (let name of sorted) {
			if (lower.startsWith(name.toLowerCase())) {
				let after = rest[name.length];
				if (after === undefined || !/[\w-]/.test(after)) {
					return rest.slice(0, name.length);
				}
			}
		}
		return null;
	}

	// Reads the plain text of a node subtree the way `text` is defined: text nodes verbatim, <br> as a
	// newline, elements recursed. Used both to read the whole editor and to measure a caret offset.
	function extractText(node: Node): string {
		let out = '';
		node.childNodes.forEach((child) => {
			if (child.nodeType === Node.TEXT_NODE) {
				out += child.nodeValue ?? '';
			} else if (child.nodeName === 'BR') {
				out += '\n';
			} else if (child.nodeType === Node.ELEMENT_NODE) {
				out += extractText(child);
			}
		});
		return out;
	}

	// The caret's character offset from the start of the editor (counting <br> as one char), computed
	// by cloning the range from the editor start to the caret and measuring it the same way as `text`.
	function caretOffset(): number {
		let root = editorRef;
		let sel = window.getSelection();
		if (!root || !sel || sel.rangeCount === 0) {
			return text.length;
		}
		let range = sel.getRangeAt(0);
		let pre = range.cloneRange();
		pre.selectNodeContents(root);
		pre.setEnd(range.endContainer, range.endOffset);
		return extractText(pre.cloneContents()).length;
	}

	// Places the caret at character offset `offset` (counting <br> as one char) within the editor.
	function setCaret(offset: number) {
		let root = editorRef;
		let sel = window.getSelection();
		if (!root || !sel) {
			return;
		}

		let range = document.createRange();
		let remaining = offset;
		let it = document.createNodeIterator(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
		let node = it.nextNode();
		let lastText: Text | null = null;
		let placed = false;

		while (node) {
			if (node.nodeType === Node.TEXT_NODE) {
				let len = node.nodeValue?.length ?? 0;
				lastText = node as Text;
				if (remaining <= len) {
					range.setStart(node, remaining);
					placed = true;
					break;
				}
				remaining -= len;
			} else if (node.nodeName === 'BR') {
				if (remaining === 0) {
					range.setStartBefore(node);
					placed = true;
					break;
				}
				remaining -= 1;
			}
			node = it.nextNode();
		}

		if (!placed) {
			if (lastText) {
				range.setStart(lastText, lastText.nodeValue?.length ?? 0);
			} else {
				range.setStart(root, root.childNodes.length);
			}
		}

		range.collapse(true);
		sel.removeAllRanges();
		sel.addRange(range);
	}

	// Re-renders the editor from `text`, optionally restoring the caret to a character offset.
	function render(offset?: number) {
		if (!editorRef) {
			return;
		}
		editorRef.innerHTML = highlightHtml(text);
		if (offset !== undefined) {
			setCaret(offset);
		}
	}

	// The caret's [start, end) character range (start === end when it is a plain caret).
	function selectionRange(): { start: number; end: number } {
		let root = editorRef;
		let sel = window.getSelection();
		if (!root || !sel || sel.rangeCount === 0) {
			return { start: text.length, end: text.length };
		}
		let range = sel.getRangeAt(0);
		let pre = range.cloneRange();
		pre.selectNodeContents(root);
		pre.setEnd(range.startContainer, range.startOffset);
		let start = extractText(pre.cloneContents()).length;
		return { start, end: caretOffset() };
	}

	// ---- Autocomplete ----------------------------------------------------------------------------
	// Staged slash-command completion:
	//  - KIND: "/" or "/<letters>" offers the command kinds (/c Components, /cl Clusters, /t Tools).
	//  - COMP-TAB / COMP-NAME: "/c/" offers tabs, then "/c/<tab>/" offers that tab's components.
	//  - CLUSTER / TOOL: "/cl/" and "/t/" offer cluster / tool names.
	// Accepting a kind or a tab inserts the next marker and immediately opens the following stage.
	type RefStage = 'kind' | 'comp-tab' | 'comp-name' | 'cluster' | 'tool' | 'memory-scope';

	let refMenuOpen = $state(false);
	let refStage = $state<RefStage>('kind');
	let refMatches = $state<string[]>([]);
	let refActiveIndex = $state(0);
	let refTokenStart = 0;
	let refTokenEnd = 0;

	function tabNames(): string[] {
		return componentTabs.map((t) => t.tab);
	}

	function componentsFor(tab: string): string[] {
		return componentTabs.find((t) => t.tab.toLowerCase() === tab.toLowerCase())?.components ?? [];
	}

	function componentNamesFlat(): string[] {
		return componentTabs.flatMap((t) => t.components);
	}

	function availableKinds(): string[] {
		let kinds: string[] = [];
		if (componentTabs.length > 0) kinds.push('c');
		if (clusterNames.length > 0) kinds.push('cl');
		if (toolNames.length > 0) kinds.push('t');
		kinds.push(...BUILTIN_COMMANDS);
		return kinds;
	}

	function kindLabel(key: string): string {
		return key === 'c'
			? 'Components'
			: key === 'cl'
				? 'Clusters'
				: key === 't'
					? 'Tools'
					: key === 'export'
						? 'Export conversation (.txt)'
						: key;
	}

	function startsWith(candidates: string[], q: string): string[] {
		let lower = q.toLowerCase();
		return candidates.filter((c) => c.toLowerCase().startsWith(lower));
	}

	function openMenu(stage: RefStage, matches: string[], start: number, end: number) {
		refStage = stage;
		refMatches = matches;
		refActiveIndex = 0;
		refTokenStart = start;
		refTokenEnd = end;
		refMenuOpen = true;
	}

	function syncRefMenu() {
		if (!editorRef) {
			refMenuOpen = false;
			return;
		}

		let caret = caretOffset();
		let before = text.slice(0, caret);
		let m: RegExpMatchArray | null;

		// /c/<tab>/<component>
		if ((m = before.match(/(?:^|\s)\/c\/([^/\n]+)\/([^\n]*)$/))) {
			let matches = startsWith(componentsFor(m[1]), m[2]);
			if (matches.length) return openMenu('comp-name', matches, caret - m[2].length, caret);
			refMenuOpen = false;
			return;
		}
		// /c/<tab>
		if ((m = before.match(/(?:^|\s)\/c\/([^/\n]*)$/))) {
			let matches = startsWith(tabNames(), m[1]);
			if (matches.length) return openMenu('comp-tab', matches, caret - m[1].length, caret);
			refMenuOpen = false;
			return;
		}
		// /cl/<cluster>
		if ((m = before.match(/(?:^|\s)\/cl\/([^\n]*)$/))) {
			let matches = startsWith(clusterNames, m[1]);
			if (matches.length) return openMenu('cluster', matches, caret - m[1].length, caret);
			refMenuOpen = false;
			return;
		}
		// /t/memory/<scope> — the memory tool's global/local scope (checked before the generic tool case)
		if (hasMemoryTool() && (m = before.match(/(?:^|\s)\/t\/memory\/([^\n]*)$/i))) {
			let matches = startsWith(MEMORY_SCOPES, m[1]);
			if (matches.length) return openMenu('memory-scope', matches, caret - m[1].length, caret);
			refMenuOpen = false;
			return;
		}
		// /t/<tool>
		if ((m = before.match(/(?:^|\s)\/t\/([^\n]*)$/))) {
			let matches = startsWith(toolNames, m[1]);
			if (matches.length) return openMenu('tool', matches, caret - m[1].length, caret);
			refMenuOpen = false;
			return;
		}
		// /<letters> — the kind menu
		if ((m = before.match(/(?:^|\s)\/([a-zA-Z]*)$/))) {
			let matches = availableKinds().filter((k) => k.startsWith(m![1].toLowerCase()));
			if (matches.length) return openMenu('kind', matches, caret - m[1].length - 1, caret);
			refMenuOpen = false;
			return;
		}

		refMenuOpen = false;
	}

	// Accepts the highlighted item. Kind and tab insert the next marker and re-open the next stage;
	// component/cluster/tool insert the name plus a trailing space.
	async function acceptRef(item: string) {
		let insert: string;
		let trail = '';
		let reopen = false;

		if (refStage === 'kind' && !BUILTIN_COMMANDS.includes(item)) {
			insert = `/${item}/`;
			reopen = true;
		} else if (refStage === 'comp-tab') {
			insert = `${item}/`;
			reopen = true;
		} else if (refStage === 'tool' && item.toLowerCase() === MEMORY_TOOL) {
			// The memory tool takes a scope — insert "memory/" and drill into global/local.
			insert = `${item}/`;
			reopen = true;
		} else {
			// A name — or a built-in command, which is complete on its own ("/export").
			insert = refStage === 'kind' ? `/${item}` : item;
			let after = text.slice(refTokenEnd);
			trail = after.length === 0 || !/^\s/.test(after) ? ' ' : '';
		}

		let piece = insert + trail;
		text = text.slice(0, refTokenStart) + piece + text.slice(refTokenEnd);
		let caret = refTokenStart + piece.length;
		refMenuOpen = false;

		await tick();
		editorRef?.focus();
		render(caret);

		if (reopen) {
			syncRefMenu();
		}
	}

	function handleRefMenuKey(e: KeyboardEvent): boolean {
		if (!refMenuOpen || refMatches.length === 0) {
			return false;
		}
		if (e.key === 'ArrowDown') {
			e.preventDefault();
			refActiveIndex = (refActiveIndex + 1) % refMatches.length;
			return true;
		}
		if (e.key === 'ArrowUp') {
			e.preventDefault();
			refActiveIndex = (refActiveIndex - 1 + refMatches.length) % refMatches.length;
			return true;
		}
		if (e.key === 'Enter' || e.key === 'Tab') {
			e.preventDefault();
			void acceptRef(refMatches[refActiveIndex]);
			return true;
		}
		if (e.key === 'Escape') {
			e.preventDefault();
			refMenuOpen = false;
			return true;
		}
		return false;
	}

	// ---- Editing --------------------------------------------------------------------------------

	// Replaces the current selection (or inserts at the caret) with `s`, then re-renders and re-syncs.
	function insertText(s: string) {
		let { start, end } = selectionRange();
		text = text.slice(0, start) + s + text.slice(end);
		editorRef?.focus();
		render(start + s.length);
		syncRefMenu();
	}

	// Inserts an [image#N] token at the caret, padded so it never glues onto adjacent words.
	async function insertToken(token: string) {
		let { start, end } = selectionRange();
		let before = text.slice(0, start);
		let after = text.slice(end);
		let lead = before.length > 0 && !/\s$/.test(before) ? ' ' : '';
		let trail = after.length > 0 && !/^\s/.test(after) ? ' ' : '';
		let piece = `${lead}${token}${trail}`;
		text = before + piece + after;
		await tick();
		editorRef?.focus();
		render(start + piece.length);
	}

	function onInput() {
		if (composing || !editorRef) {
			return;
		}
		let caret = caretOffset();
		text = extractText(editorRef);
		render(caret);
		syncRefMenu();
	}

	function onCompositionStart() {
		composing = true;
	}

	function onCompositionEnd() {
		composing = false;
		onInput();
	}

	async function addImages(files: FileList | File[] | null | undefined) {
		// The single choke point for every intake path (picker, paste, drop): no Add Image tool
		// wired means no image enters the composer, however it arrived.
		if (!files || !imageToolWired) {
			return;
		}
		let images = Array.from(files).filter((f) => f.type.startsWith('image/'));
		for (let file of images) {
			let dataUrl: string;
			try {
				dataUrl = await readAsDataUrl(file);
			} catch {
				continue;
			}
			pending.push({
				id: nextId++,
				base64: stripDataUrl(dataUrl),
				mediaType: file.type || 'image/png',
				filename: file.name || 'image',
				source: 'user'
			});
			await insertToken(`[image#${pending.length}]`);
		}
	}

	// Attaches a viewport snapshot captured by the host's geometry button (attach mode), landing it in
	// the strip with an [image#N] token exactly like a pasted image — the user then types the message
	// it belongs to. Invoked from outside via bind:this, so it re-checks its own gate: the Geometry
	// Snapshot tool grants this lane, and the Add Image tool is irrelevant to it.
	export async function addSnapshot(base64: string, mediaType: string) {
		if (!snapshotAttachWired || !base64) {
			return;
		}
		pending.push({
			id: nextId++,
			base64,
			mediaType: mediaType || 'image/png',
			filename: 'geometry-snapshot.png',
			source: 'snapshot'
		});
		await insertToken(`[image#${pending.length}]`);
	}

	// Remove a pending image: drop the matching token and renumber the rest so tokens stay 1..N.
	function removeImage(index: number) {
		dropImagesAt(new Set([index]));
	}

	// Drops the pending images at the given positions, deleting their [image#N] tokens and renumbering
	// the survivors so the tokens stay 1..N. Returns early on an empty set — which also keeps `text`
	// out of the calling effect's dependencies, so typing does not re-run the stale-attachment scan.
	function dropImagesAt(positions: Set<number>) {
		if (positions.size === 0) {
			return;
		}
		pending = pending.filter((_, i) => !positions.has(i));
		let occurrence = 0;
		let kept = 0;
		text = text.replace(TOKEN, () => {
			if (positions.has(occurrence++)) {
				return '';
			}
			kept++;
			return `[image#${kept}]`;
		});
		text = tidy(text);
		void tick().then(() => render());
	}

	function tidy(value: string): string {
		return value.replace(/[ \t]{2,}/g, ' ').replace(/[ \t]+$/gm, '');
	}

	// Sends the box's contents (or saves a pasted API key). Also invoked from outside via
	// bind:this — the submit button lives in App's right-hand rail, not in this component.
	export function submit() {
		if (inert) {
			return;
		}

		if (apiKeyProvider) {
			let key = text.trim();
			if (!key) {
				return;
			}
			onsavekey?.(apiKeyProvider.id, key);
			text = '';
			pending = [];
			render(0);
			return;
		}

		refMenuOpen = false;
		let sentText = tidy(text.replace(TOKEN, '')).trim();
		let images = pending.map((p) => ({
			base64: p.base64,
			mediaType: p.mediaType,
			filename: p.filename
		}));

		if (!sentText && images.length === 0) {
			return;
		}

		onsend({ text: sentText, images });
		text = '';
		pending = [];
		render(0);
	}

	function onKeyDown(e: KeyboardEvent) {
		if (handleRefMenuKey(e)) {
			return;
		}
		if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
			e.preventDefault();
			submit();
			return;
		}
		if (e.key === 'Enter' && e.shiftKey) {
			// Insert a newline ourselves so the DOM stays canonical (browser Enter injects <div>/<br>).
			e.preventDefault();
			insertText('\n');
		}
	}

	function onKeyUp(e: KeyboardEvent) {
		if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Escape') {
			return;
		}
		syncRefMenu();
	}

	function onPaste(e: ClipboardEvent) {
		let items = e.clipboardData?.items;
		let files: File[] = [];
		if (items && imageToolWired) {
			for (let i = 0; i < items.length; i++) {
				if (items[i].kind === 'file') {
					let file = items[i].getAsFile();
					if (file) {
						files.push(file);
					}
				}
			}
		}

		if (files.length > 0) {
			e.preventDefault();
			void addImages(files);
			return;
		}

		// Plain-text paste: insert it ourselves so no rich markup enters the contenteditable.
		let pasted = e.clipboardData?.getData('text/plain') ?? '';
		if (pasted) {
			e.preventDefault();
			insertText(pasted);
		}
	}

	// Opens the image file picker. Invoked from outside via bind:this — the Add Image button
	// lives in App's right-hand rail — while the picker's <input> stays here with the intake logic.
	export function openPicker() {
		fileInputRef?.click();
	}

	function onFileChange(e: Event) {
		let input = e.currentTarget as HTMLInputElement;
		void addImages(input.files);
		input.value = '';
	}
</script>

{#if pending.length > 0}
	<div class="flex flex-wrap gap-2 pb-2">
		{#each pending as image, i (image.id)}
			<div class="relative">
				<img
					src={`data:${image.mediaType};base64,${image.base64}`}
					alt={image.filename}
					class="neu-raised-sm h-12 w-12 rounded-md object-cover"
				/>
				<button
					type="button"
					onclick={() => removeImage(i)}
					title="Remove image"
					class="neu-btn text-muted-foreground hover:text-foreground absolute -top-1.5 -right-1.5 flex size-4 items-center justify-center rounded-full"
				>
					<XIcon class="size-3" />
				</button>
			</div>
		{/each}
	</div>
{/if}

<!-- flex-1 stretches the box to fill App's bottom-row column, so its top and bottom edges stay
     pinned to the action stack on its right even when the stack is the taller of the two. -->
<div class="relative flex flex-1 flex-col">
	{#if refMenuOpen}
		<div
			class="neu-raised absolute bottom-full left-0 z-10 mb-1.5 max-h-56 w-full overflow-y-auto rounded-lg p-1"
		>
			{#each refMatches as item, i (item)}
				<button
					type="button"
					class={`flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm ${
						i === refActiveIndex ? 'bg-muted-foreground/15' : 'hover:bg-muted-foreground/10'
					}`}
					onmousedown={(e) => {
						e.preventDefault();
						void acceptRef(item);
					}}
				>
					{#if refStage === 'kind'}
						{#if item === 'export'}
							<DownloadIcon class="text-muted-foreground size-3.5 shrink-0" />
						{:else if item === 't'}
							<WrenchIcon class="text-muted-foreground size-3.5 shrink-0" />
						{:else if item === 'cl'}
							<BoxIcon class="text-muted-foreground size-3.5 shrink-0" />
						{:else}
							<ShapesIcon class="text-muted-foreground size-3.5 shrink-0" />
						{/if}
						<span class="flex-1 truncate">{kindLabel(item)}</span>
						<span class="text-muted-foreground/70 font-mono text-xs"
							>{BUILTIN_COMMANDS.includes(item) ? `/${item}` : `/${item}/`}</span
						>
					{:else if refStage === 'comp-tab'}
						<LayersIcon class="text-muted-foreground size-3.5 shrink-0" />
						<span class="flex-1 truncate">{item}</span>
					{:else if refStage === 'comp-name'}
						<ShapesIcon class="text-muted-foreground size-3.5 shrink-0" />
						<span class="flex-1 truncate">{item}</span>
					{:else if refStage === 'cluster'}
						<BoxIcon class="text-muted-foreground size-3.5 shrink-0" />
						<span class="flex-1 truncate">{item}</span>
						<span class="text-muted-foreground/70 font-mono text-xs">/cl/</span>
					{:else if refStage === 'memory-scope'}
						<BrainIcon class="text-muted-foreground size-3.5 shrink-0" />
						<span class="flex-1 truncate">{item}</span>
						<span class="text-muted-foreground/70 font-mono text-xs">/t/memory/</span>
					{:else}
						<WrenchIcon class="text-muted-foreground size-3.5 shrink-0" />
						<span class="flex-1 truncate">{item}</span>
						<span class="text-muted-foreground/70 font-mono text-xs">/t/</span>
					{/if}
				</button>
			{/each}
		</div>
	{/if}

	<!-- The prompt box is the editor alone — the send / cancel / clear buttons live in App's
	     action stack to the right of this box, and the human tools along the window's top row. -->
	<div class="neu-well flex flex-1 flex-col rounded-xl p-2">
		<!-- Contenteditable prompt editor. Sans by default; "/" commands render monospace-on-chip
		     (.slash-cmd, styled in app.css). The caret lives in the styled content, so per-token fonts
		     do not drift it. -->
		<div
			bind:this={editorRef}
			class="prompt-editor max-h-56 min-h-16 flex-1 resize-none overflow-y-auto whitespace-pre-wrap break-words p-2 text-base focus:outline-none"
			class:opacity-60={inert}
			contenteditable={!inert}
			role="textbox"
			aria-multiline="true"
			tabindex="0"
			data-placeholder={placeholder}
			oninput={onInput}
			onkeydown={onKeyDown}
			onkeyup={onKeyUp}
			onclick={syncRefMenu}
			onpaste={onPaste}
			oncompositionstart={onCompositionStart}
			oncompositionend={onCompositionEnd}
		></div>
	</div>
</div>

{#if imageToolWired}
	<input
		bind:this={fileInputRef}
		type="file"
		accept="image/*"
		multiple
		class="hidden"
		onchange={onFileChange}
	/>
{/if}

<style>
	/* Placeholder for the empty contenteditable (it has no native placeholder). Scoped styles reach
	   the template element (unlike the {@html}-injected .slash-cmd spans, which app.css styles). */
	.prompt-editor:empty::before {
		content: attr(data-placeholder);
		color: var(--muted-foreground);
		pointer-events: none;
	}
</style>
