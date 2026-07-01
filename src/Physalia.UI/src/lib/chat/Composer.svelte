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

	// "/c/<clustername>" reference autocomplete. The menu opens while the user is typing a "/c/" token
	// (at a word boundary) and offers the cluster names the model may use; only these can be inserted,
	// so a token always names a real, included cluster. The server normalizes the token on submit.
	let clusterMenuOpen = $state(false);
	let clusterMatches = $state<string[]>([]);
	let clusterActiveIndex = $state(0);
	// Text range [start, end) of the "/c/<query>" being typed, replaced when a name is chosen.
	let clusterTokenStart = 0;
	let clusterTokenEnd = 0;

	// Recomputes the autocomplete menu from the text before the caret. A "/c/" at the start or after
	// whitespace, followed by any non-newline text, is the active query; matches are cluster names
	// that start with it (case-insensitive). No match (or no clusters) closes the menu.
	function syncClusterMenu() {
		let el = textareaRef;
		if (!el || clusterNames.length === 0) {
			clusterMenuOpen = false;
			return;
		}

		let caret = el.selectionStart ?? text.length;
		let before = text.slice(0, caret);
		let m = before.match(/(?:^|\s)\/c\/([^\n]*)$/);
		if (!m) {
			clusterMenuOpen = false;
			return;
		}

		let query = m[1];
		let matches = clusterNames.filter((n) => n.toLowerCase().startsWith(query.toLowerCase()));
		if (matches.length === 0) {
			clusterMenuOpen = false;
			return;
		}

		clusterMatches = matches;
		clusterActiveIndex = 0;
		clusterTokenStart = caret - query.length - 3; // back over "/c/" + the query
		clusterTokenEnd = caret;
		clusterMenuOpen = true;
	}

	// Replaces the in-progress "/c/<query>" with "/c/<name>" (kept as a token so the host resolves it),
	// adds a trailing space, and restores the caret after it.
	async function acceptCluster(name: string) {
		let token = `/c/${name}`;
		let after = text.slice(clusterTokenEnd);
		let trail = after.length === 0 || !/^\s/.test(after) ? ' ' : '';
		text = text.slice(0, clusterTokenStart) + token + trail + after;
		let caret = clusterTokenStart + token.length + trail.length;
		clusterMenuOpen = false;

		await tick();
		let el = textareaRef;
		if (el) {
			el.focus();
			el.selectionStart = el.selectionEnd = caret;
		}
	}

	// Menu keyboard navigation; returns true when the key was consumed (so the caller skips its own
	// Enter-to-send / newline handling).
	function handleClusterMenuKey(e: KeyboardEvent): boolean {
		if (!clusterMenuOpen || clusterMatches.length === 0) {
			return false;
		}

		if (e.key === 'ArrowDown') {
			e.preventDefault();
			clusterActiveIndex = (clusterActiveIndex + 1) % clusterMatches.length;
			return true;
		}
		if (e.key === 'ArrowUp') {
			e.preventDefault();
			clusterActiveIndex = (clusterActiveIndex - 1 + clusterMatches.length) % clusterMatches.length;
			return true;
		}
		if (e.key === 'Enter' || e.key === 'Tab') {
			e.preventDefault();
			void acceptCluster(clusterMatches[clusterActiveIndex]);
			return true;
		}
		if (e.key === 'Escape') {
			e.preventDefault();
			clusterMenuOpen = false;
			return true;
		}

		return false;
	}

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

		clusterMenuOpen = false;
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
		// The cluster autocomplete menu owns arrows/Enter/Tab/Escape while it is open.
		if (handleClusterMenuKey(e)) {
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
		syncClusterMenu();
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
	{#if clusterMenuOpen}
		<div
			class="neu-raised absolute bottom-full left-0 z-10 mb-1.5 max-h-56 w-full overflow-y-auto rounded-lg p-1"
		>
			{#each clusterMatches as name, i (name)}
				<button
					type="button"
					class={`flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm ${
						i === clusterActiveIndex ? 'bg-muted-foreground/15' : 'hover:bg-muted-foreground/10'
					}`}
					onmousedown={(e) => {
						e.preventDefault();
						void acceptCluster(name);
					}}
				>
					<BoxIcon class="text-muted-foreground size-3.5 shrink-0" />
					<span class="flex-1 truncate">{name}</span>
					<span class="text-muted-foreground/70 font-mono text-xs">/c/</span>
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

	<Textarea
		bind:ref={textareaRef}
		bind:value={text}
		{placeholder}
		disabled={inert}
		onkeydown={onKeyDown}
		onkeyup={onKeyUp}
		onclick={syncClusterMenu}
		onpaste={onPaste}
		class="max-h-56 min-h-16 flex-1 resize-none border-none bg-transparent p-2 text-base md:text-base shadow-none focus-visible:ring-0 disabled:bg-transparent disabled:opacity-100 dark:bg-transparent"
	/>

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
