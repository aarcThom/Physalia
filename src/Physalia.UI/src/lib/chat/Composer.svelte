<script lang="ts">
	// The prompt box. Image intake (file picker, clipboard paste, drag-and-drop) is handled
	// here directly — converted to base64 via FileReader, with no crypto.randomUUID / blob
	// URLs, which fail in the file:// WebView the chat runs inside. Each added image inserts
	// a Claude-Code-style "[image#N]" token at the caret and a thumbnail in the strip above
	// the textarea. The tokens are composer-only scaffolding: they're stripped from the text
	// before it's sent (the images travel as real image blocks via the bridge).
	import { tick } from 'svelte';
	import { Textarea } from '$lib/components/ui/textarea/index.js';
	import { Button } from '$lib/components/ui/button/index.js';
	import ImagePlusIcon from '@lucide/svelte/icons/image-plus';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import SquareIcon from '@lucide/svelte/icons/square';
	import XIcon from '@lucide/svelte/icons/x';
	import LayersIcon from '@lucide/svelte/icons/layers';
	import BoxIcon from '@lucide/svelte/icons/box';
	import WrenchIcon from '@lucide/svelte/icons/wrench';
	import { stripDataUrl, type SubmitMessage } from '$lib/bridge';

	interface Props {
		/** No Recorder wired — shown as a hint, but the box stays usable (sending
		 *  still mints a Prompt Signal). */
		disconnected: boolean;
		/** Pipeline is mid-run — block input until it settles, to avoid re-submitting. */
		busy: boolean;
		/** Setup not finished (no provider configured) — block input; there's nothing to send to. */
		disabled?: boolean;
		/** When set, the box captures an API key for this provider instead of sending a message. */
		apiKeyProvider?: { id: string; label: string } | null;
		/** True when any grounding (components, clusters, or document units) is wired — enables the grounding button. */
		groundingWired?: boolean;
		/** Names of clusters the model may use, for the "/c/" reference autocomplete. */
		clusterNames?: string[];
		/** Names of tools currently in use, for the "/t/" reference autocomplete. */
		toolNames?: string[];
		onsend: (message: SubmitMessage) => void;
		/** Called with the pasted API key when in apiKeyProvider mode. */
		onsavekey?: (providerId: string, key: string) => void;
		/** Opens the grounding selection panel. */
		ongrounding?: () => void;
		/** Cancels the in-flight request; only invokable while busy. */
		oncancel?: () => void;
	}

	let {
		disconnected,
		busy,
		disabled = false,
		apiKeyProvider = null,
		groundingWired = false,
		clusterNames = [],
		toolNames = [],
		onsend,
		onsavekey,
		ongrounding,
		oncancel
	}: Props = $props();

	interface PendingImage {
		id: number;
		base64: string;
		mediaType: string;
		filename: string;
	}

	// Block while the pipeline is busy, during setup (no provider yet), or while no Recorder is
	// wired (nothing to send to) — EXCEPT in API-key mode, where the box stays live so the user
	// can paste their key. The connect screen offers the buttons to wire a Recorder instead.
	let inert = $derived(busy || ((disabled || disconnected) && !apiKeyProvider));

	let text = $state('');
	let pending = $state<PendingImage[]>([]);
	let textareaRef = $state<HTMLTextAreaElement | null>(null);
	let fileInputRef = $state<HTMLInputElement | null>(null);
	let nextId = 0;

	let placeholder = $derived(
		apiKeyProvider
			? `Paste your ${apiKeyProvider.label} API key here, then press Enter`
			: disabled
				? 'Finish setup to start chatting…'
				: disconnected
					? '' // no Recorder wired: the box is greyed out with no prompt text
					: 'Send a message…  (Enter to send, Shift+Enter for a new line)'
	);

	// All [image#N] tokens. Order in the text mirrors insertion order, which mirrors `pending`.
	const TOKEN = /\[image#\d+\]/g;

	// Clear the box whenever API-key mode is entered, left, or switched between providers, so a
	// typed key never carries over into a chat message (or another provider's key field).
	let prevKeyProviderId = $state<string | null>(null);
	$effect(() => {
		const id = apiKeyProvider?.id ?? null;
		if (id !== prevKeyProviderId) {
			prevKeyProviderId = id;
			text = '';
			pending = [];
		}
	});

	// Whole-window drag-and-drop (mirrors the old globalDrop). Document-level covers the root.
	$effect(() => {
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

	// Insert a token at the caret (or append), padding so it never glues onto adjacent words,
	// then restore the caret just after it.
	async function insertToken(token: string) {
		let el = textareaRef;
		let start = el?.selectionStart ?? text.length;
		let end = el?.selectionEnd ?? text.length;
		let before = text.slice(0, start);
		let after = text.slice(end);
		let lead = before.length > 0 && !/\s$/.test(before) ? ' ' : '';
		let trail = after.length > 0 && !/^\s/.test(after) ? ' ' : '';
		let piece = `${lead}${token}${trail}`;
		text = before + piece + after;

		let caret = before.length + piece.length;
		await tick();
		if (el) {
			el.focus();
			el.selectionStart = el.selectionEnd = caret;
		}
	}

	// Slash-command autocomplete. Two levels:
	//  - KIND: typing "/" (at a word boundary) offers the available command kinds — "/t Tools",
	//    "/c Clusters" — filtered by what you have typed ("/t" narrows to Tools). Accepting inserts
	//    the "/x/" marker and immediately opens the NAME menu.
	//  - NAME: typing "/c/" or "/t/" offers the cluster / tool names the model may use; only these can
	//    be inserted, so a token always names a real, available reference. The host normalizes the
	//    token on submit.
	let refMenuOpen = $state(false);
	let refMode = $state<'kind' | 'name'>('name');
	let refMatches = $state<string[]>([]); // kind keys ('c' | 't') in kind mode; names in name mode
	let refActiveIndex = $state(0);
	let refMarker = $state<'c' | 't'>('c'); // which reference kind the NAME menu is completing
	// Text range [start, end) of the slash token being typed, replaced when an item is chosen.
	let refTokenStart = 0;
	let refTokenEnd = 0;

	function namesFor(marker: 'c' | 't'): string[] {
		return marker === 'c' ? clusterNames : toolNames;
	}

	function kindLabel(key: string): string {
		return key === 't' ? 'Tools' : key === 'c' ? 'Clusters' : key;
	}

	// The command kinds that currently have candidates, in menu order.
	function availableKinds(): string[] {
		let kinds: string[] = [];
		if (toolNames.length > 0) kinds.push('t');
		if (clusterNames.length > 0) kinds.push('c');
		return kinds;
	}

	// Recomputes the autocomplete menu from the text before the caret. A "/c/" or "/t/" (with the
	// second slash) drives the NAME menu; a bare "/" or "/<letters>" at a word boundary drives the KIND
	// menu. No match (or no candidates) closes the menu.
	function syncRefMenu() {
		let el = textareaRef;
		if (!el) {
			refMenuOpen = false;
			return;
		}

		let caret = el.selectionStart ?? text.length;
		let before = text.slice(0, caret);

		// NAME menu: "/<marker>/<query>" — takes precedence over the kind menu (has the second slash).
		let name = before.match(/(?:^|\s)\/([ct])\/([^\n]*)$/);
		if (name) {
			let marker = name[1] as 'c' | 't';
			let query = name[2];
			let matches = namesFor(marker).filter((n) => n.toLowerCase().startsWith(query.toLowerCase()));
			if (matches.length === 0) {
				refMenuOpen = false;
				return;
			}

			refMode = 'name';
			refMarker = marker;
			refMatches = matches;
			refActiveIndex = 0;
			refTokenStart = caret - query.length - 3; // back over "/<marker>/" + the query
			refTokenEnd = caret;
			refMenuOpen = true;
			return;
		}

		// KIND menu: "/" or "/<letters>" (no second slash) at a word boundary.
		let kind = before.match(/(?:^|\s)\/([a-zA-Z]*)$/);
		if (kind) {
			let typed = kind[1].toLowerCase();
			let matches = availableKinds().filter((k) => k.startsWith(typed));
			if (matches.length === 0) {
				refMenuOpen = false;
				return;
			}

			refMode = 'kind';
			refMatches = matches;
			refActiveIndex = 0;
			refTokenStart = caret - typed.length - 1; // back over "/" + the typed letters
			refTokenEnd = caret;
			refMenuOpen = true;
			return;
		}

		refMenuOpen = false;
	}

	// Accepts the highlighted item. In KIND mode it inserts the "/x/" marker and re-opens the NAME menu;
	// in NAME mode it inserts "/<marker>/<name>" (kept as a token so the host resolves it) plus a
	// trailing space. Restores the caret after the inserted text.
	async function acceptRef(item: string) {
		let token: string;
		let trail: string;
		if (refMode === 'kind') {
			token = `/${item}/`;
			trail = '';
		} else {
			token = `/${refMarker}/${item}`;
			let after = text.slice(refTokenEnd);
			trail = after.length === 0 || !/^\s/.test(after) ? ' ' : '';
		}

		text = text.slice(0, refTokenStart) + token + trail + text.slice(refTokenEnd);
		let caret = refTokenStart + token.length + trail.length;
		refMenuOpen = false;

		await tick();
		let el = textareaRef;
		if (el) {
			el.focus();
			el.selectionStart = el.selectionEnd = caret;
		}

		// After choosing a kind, immediately offer its names.
		if (refMode === 'kind') {
			syncRefMenu();
		}
	}

	// Menu keyboard navigation; returns true when the key was consumed (so the caller skips its own
	// Enter-to-send / newline handling).
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

	// ---- Slash-command highlighting -------------------------------------------------------------
	// A transparent-text textarea sits over a mirrored backdrop that renders the same text with every
	// Physalia "/" command coloured purple. The backdrop must match the textarea's font, padding and
	// wrapping exactly (see the markup) and is scroll-synced so the colours track the caret.
	let highlightRef = $state<HTMLDivElement | null>(null);

	function escapeHtml(s: string): string {
		return s
			.replace(/&/g, '&amp;')
			.replace(/</g, '&lt;')
			.replace(/>/g, '&gt;');
	}

	// Longest-first, case-insensitive; requires a non-word boundary after the name (so "/c/Truss"
	// does not match inside "/c/Trusses"). Mirrors the C# PromptClusterResolver / PromptToolResolver.
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

	// Splits the text into plain / command segments. A command starts at a "/" on a word boundary:
	// "/c/" or "/t/" extends across a known (possibly multi-word) reference name, else across the run
	// of non-whitespace after the marker; a bare "/token" (e.g. an image alias) colours that token.
	function highlightHtml(value: string): string {
		let out = '';
		let plainStart = 0;
		let i = 0;

		let flushPlain = (upto: number) => {
			if (upto > plainStart) {
				out += escapeHtml(value.slice(plainStart, upto));
			}
		};

		while (i < value.length) {
			let atBoundary = i === 0 || /\s/.test(value[i - 1]);
			if (value[i] === '/' && atBoundary) {
				let marker3 = value.slice(i, i + 3).toLowerCase();
				let end: number;
				if (marker3 === '/c/' || marker3 === '/t/') {
					let rest = value.slice(i + 3);
					let matched = matchKnownName(rest, marker3 === '/c/' ? clusterNames : toolNames);
					if (matched !== null) {
						end = i + 3 + matched.length;
					} else {
						end = i + 3;
						while (end < value.length && !/\s/.test(value[end])) end++;
					}
				} else {
					end = i + 1;
					while (end < value.length && !/\s/.test(value[end])) end++;
				}

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
		// A trailing newline needs a placeholder char, else the backdrop is one line shorter than the
		// textarea and the last line's colours drift out of sync.
		return value.endsWith('\n') ? out + ' ' : out;
	}

	let highlighted = $derived(highlightHtml(text));

	// Keep the backdrop scrolled in lock-step with the textarea.
	function syncScroll() {
		let el = textareaRef;
		let bg = highlightRef;
		if (el && bg) {
			bg.scrollTop = el.scrollTop;
			bg.scrollLeft = el.scrollLeft;
		}
	}

	// Text changes (typing, paste, token insert/remove) can shift the textarea's scroll; re-sync the
	// backdrop after the DOM updates so the colours stay aligned with the caret.
	$effect(() => {
		highlighted;
		void tick().then(syncScroll);
	});

	async function addImages(files: FileList | File[] | null | undefined) {
		if (!files) {
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
				filename: file.name || 'image'
			});
			await insertToken(`[image#${pending.length}]`);
		}
	}

	// Remove a pending image: drop the matching (index-th) token from the text and renumber
	// the rest so the strip and tokens stay 1..N contiguous. The strip is the source of truth.
	function removeImage(index: number) {
		pending.splice(index, 1);

		let occurrence = 0;
		let kept = 0;
		text = text.replace(TOKEN, () => {
			occurrence++;
			if (occurrence === index + 1) {
				return ''; // the removed image's token
			}
			kept++;
			return `[image#${kept}]`;
		});
		text = tidy(text);
	}

	// Collapse runs of spaces/tabs left by token removal and trim trailing space per line.
	function tidy(value: string): string {
		return value.replace(/[ \t]{2,}/g, ' ').replace(/[ \t]+$/gm, '');
	}

	function submit() {
		if (inert) {
			return;
		}

		// API-key mode: the box content IS the key — hand it to the host, don't send a message.
		if (apiKeyProvider) {
			let key = text.trim();
			if (!key) {
				return;
			}
			onsavekey?.(apiKeyProvider.id, key);
			text = '';
			pending = [];
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
	}

	function onKeyDown(e: KeyboardEvent) {
		// The reference autocomplete menu owns arrows/Enter/Tab/Escape while it is open.
		if (handleRefMenuKey(e)) {
			return;
		}

		// Don't submit mid-IME-composition; Shift+Enter inserts a newline.
		if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
			e.preventDefault();
			submit();
		}
	}

	// Recompute the menu after the caret/text may have changed. keyup covers typing and caret moves;
	// the menu-navigation keys are handled in keydown (and consumed), so they don't reach here.
	function onKeyUp(e: KeyboardEvent) {
		if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Escape') {
			return;
		}
		syncRefMenu();
	}

	function onPaste(e: ClipboardEvent) {
		let items = e.clipboardData?.items;
		if (!items) {
			return;
		}

		let files: File[] = [];
		for (let i = 0; i < items.length; i++) {
			if (items[i].kind === 'file') {
				let file = items[i].getAsFile();
				if (file) {
					files.push(file);
				}
			}
		}

		if (files.length > 0) {
			e.preventDefault();
			void addImages(files);
		}
	}

	function openPicker() {
		fileInputRef?.click();
	}

	function onFileChange(e: Event) {
		let input = e.currentTarget as HTMLInputElement;
		void addImages(input.files);
		input.value = ''; // allow re-selecting the same file
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

<div class="relative">
	{#if refMenuOpen}
		<div
			class="neu-raised absolute bottom-full left-0 z-10 mb-1.5 max-h-56 w-full overflow-y-auto rounded-lg p-1"
		>
			{#each refMatches as item, i (item)}
				{@const key = refMode === 'kind' ? item : refMarker}
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
					{#if key === 't'}
						<WrenchIcon class="text-muted-foreground size-3.5 shrink-0" />
					{:else}
						<BoxIcon class="text-muted-foreground size-3.5 shrink-0" />
					{/if}
					<span class="flex-1 truncate">{refMode === 'kind' ? kindLabel(item) : item}</span>
					<span class="text-muted-foreground/70 font-mono text-xs">/{key}/</span>
				</button>
			{/each}
		</div>
	{/if}

	<div class="neu-well flex items-end gap-1.5 rounded-xl p-2">
		<div class="flex flex-col gap-1.5">
		<Button
			variant="ghost"
			size="icon"
			onclick={() => ongrounding?.()}
			disabled={inert || !!apiKeyProvider || !groundingWired}
			title={groundingWired
				? 'Grounding — choose what context is available to the model'
				: 'Grounding — wire a grounding (Library, Cluster, or Document Units) into the Recorder to enable'}
		>
			<LayersIcon />
		</Button>

		<Button
			variant="ghost"
			size="icon"
			onclick={openPicker}
			disabled={inert || !!apiKeyProvider}
			title="Add image"
		>
			<ImagePlusIcon />
		</Button>
	</div>

	<div class="relative flex-1">
		<!-- Mirrored backdrop: same font/padding/wrapping as the textarea, renders the coloured
		     slash-commands under the transparent-text textarea. -->
		<div
			bind:this={highlightRef}
			aria-hidden="true"
			class="pointer-events-none absolute inset-0 max-h-56 min-h-16 overflow-hidden whitespace-pre-wrap break-words p-2 font-mono text-base text-foreground md:text-base"
		>{@html highlighted}</div>

		<Textarea
			bind:ref={textareaRef}
			bind:value={text}
			{placeholder}
			disabled={inert}
			spellcheck={false}
			onkeydown={onKeyDown}
			onkeyup={onKeyUp}
			onclick={syncRefMenu}
			onpaste={onPaste}
			onscroll={syncScroll}
			class="relative max-h-56 min-h-16 w-full resize-none border-none bg-transparent p-2 font-mono text-base break-words text-transparent caret-[var(--foreground)] shadow-none focus-visible:ring-0 disabled:bg-transparent disabled:opacity-100 md:text-base dark:bg-transparent"
		/>
	</div>

	<Button
		variant="ghost"
		size="icon"
		onclick={() => oncancel?.()}
		disabled={!busy}
		title={busy ? 'Cancel the active request' : 'No active request to cancel'}
	>
		<SquareIcon />
	</Button>

	<Button size="icon" onclick={submit} disabled={inert} title="Send">
		<ArrowUpIcon />
	</Button>
	</div>
</div>

<input
	bind:this={fileInputRef}
	type="file"
	accept="image/*"
	multiple
	class="hidden"
	onchange={onFileChange}
/>
