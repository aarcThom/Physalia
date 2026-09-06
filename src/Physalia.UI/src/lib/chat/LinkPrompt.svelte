<script lang="ts">
	// The confirmation shown before a link inside a model's answer is followed. It REPLACES
	// streamdown's own link-safety modal (passed to it as `linkSafety.renderModal`), for two reasons,
	// and either one on its own would have been enough:
	//
	//  1. Its markup is styled with Tailwind classes that live in `node_modules`, which Tailwind never
	//     scans — so none of them are compiled into our bundle and the dialog rendered with no
	//     backdrop, no card and no positioning: unreadable text laid straight over the conversation.
	//  2. Its confirm calls `window.open`, which inside the Eto WebView reaches no browser. Links go
	//     out through the host (`openExternalLink`), the same path the setup page's links take.
	//
	// The dialog earns its place beyond the styling: a markdown link shows the model's LABEL, not its
	// destination ("download zipped LAS" was a webtransfer.vancouver.ca URL), so the URL is spelled
	// out here before anything opens.
	import { openExternalLink } from '$lib/bridge';
	import { UseClipboard } from '$lib/hooks/use-clipboard.svelte';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import ExternalLinkIcon from '@lucide/svelte/icons/external-link';

	interface Props {
		/** The destination, already resolved and sanitized by streamdown. */
		url: string;
		/** Whether the prompt is showing — one instance exists per intercepted link. */
		isOpen: boolean;
		/** Dismiss without opening anything. */
		onClose: () => void;
	}

	let { url, isOpen, onClose }: Props = $props();

	const clipboard = new UseClipboard({ delay: 1200 });

	function open() {
		openExternalLink(url);
		onClose();
	}

	// Escape dismisses, as it does everywhere else in the window. The listener is mounted only while
	// the prompt is up — every link in the conversation renders one of these components.
	$effect(() => {
		if (!isOpen) {
			return;
		}

		const onKey = (event: KeyboardEvent) => {
			if (event.key === 'Escape') {
				event.stopPropagation();
				onClose();
			}
		};

		document.addEventListener('keydown', onKey);
		return () => document.removeEventListener('keydown', onKey);
	});
</script>

{#if isOpen}
	<!-- Opaque enough to take the conversation out of play, and above it: the whole point of the
	     prompt is that the URL below is the only thing being read. -->
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-[oklch(0.55_0.03_236_/_0.35)] p-4"
		role="presentation"
		onclick={onClose}
	>
		<!-- Clicks inside the card are the card's own; only the scrim dismisses. -->
		<div
			class="neu-raised flex w-full max-w-sm flex-col gap-3 rounded-xl p-4 text-left"
			role="dialog"
			aria-modal="true"
			aria-label="Open external link?"
			onclick={(event) => event.stopPropagation()}
			onkeydown={(event) => event.stopPropagation()}
			tabindex="-1"
		>
			<div class="flex flex-col gap-1">
				<h2 class="text-sm font-semibold">Open external link?</h2>
				<p class="text-muted-foreground text-xs">
					This opens in your browser, outside Rhino.
				</p>
			</div>

			<div
				class="neu-well max-h-28 overflow-y-auto rounded-lg p-2.5 font-mono text-xs break-all"
			>
				{url}
			</div>

			<div class="flex items-center gap-2">
				<button
					type="button"
					class="neu-btn text-muted-foreground hover:text-foreground rounded-lg px-3 py-1.5 text-xs"
					onclick={onClose}
				>
					Cancel
				</button>
				<button
					type="button"
					class="neu-btn text-muted-foreground hover:text-foreground ml-auto flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs"
					onclick={() => void clipboard.copy(url)}
				>
					{#if clipboard.status === 'success'}
						<CheckIcon class="size-3.5" />
						Copied
					{:else}
						<CopyIcon class="size-3.5" />
						Copy link
					{/if}
				</button>
				<button
					type="button"
					class="neu-btn flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs text-[var(--neu-accent)]"
					onclick={open}
				>
					<ExternalLinkIcon class="size-3.5" />
					Open link
				</button>
			</div>
		</div>
	</div>
{/if}
