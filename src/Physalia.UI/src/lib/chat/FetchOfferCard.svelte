<script lang="ts">
	// The button offered when a download is refused by a bot challenge — a door that opens for a real
	// browser and for nothing else.
	//
	// It exists because of WHERE the user is standing. The block is discovered mid-conversation: the
	// model explains it here, and without this the only way out was to go and find the Download File
	// node on a canvas the user may not even be looking at, right-click it, pick a menu item and paste
	// the URL back in. That menu item still exists for an arbitrary URL; it was the wrong thing to be
	// the ONLY route out of a failure the chat is already talking about.
	//
	// Unlike an approval card this blocks nothing — the tool call has already finished and failed —
	// so there is no timeout and no fail-closed default. Ignoring it costs the file, not the round.
	import { answerFetchOffer, type UiFetchOffer } from '$lib/bridge';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import XIcon from '@lucide/svelte/icons/x';

	interface Props {
		/** Files waiting to be fetched in a browser, oldest first. */
		offers: UiFetchOffer[];
	}

	let { offers }: Props = $props();

	// The host clears an offer as soon as it is acted on, but the round is not waiting on this and a
	// button that stays live invites a second browser window on the same file.
	let taken = $state<Set<string>>(new Set());

	function act(offer: UiFetchOffer, open: boolean) {
		if (taken.has(offer.id)) {
			return;
		}

		taken = new Set(taken).add(offer.id);
		answerFetchOffer(offer.id, open);
	}

	// The file name, for a label that reads as the thing being fetched rather than as a URL. Falls
	// back to the host when a URL ends in a slash.
	function nameOf(url: string): string {
		try {
			const parsed = new URL(url);
			const last = parsed.pathname.split('/').filter(Boolean).pop();
			return last ?? parsed.host;
		} catch {
			return url;
		}
	}

	// All of them, not just the oldest: several blocked tiles means several files wanted, and unlike
	// a consent prompt there is no habituation risk in a list of download buttons.
</script>

{#each offers as offer (offer.id)}
	<div class="neu-raised mb-2 flex flex-col gap-2.5 rounded-xl p-3 text-left">
		<div class="flex items-start gap-2">
			<DownloadIcon class="mt-0.5 size-4 shrink-0 text-[var(--neu-accent)]" />
			<div class="flex min-w-0 flex-col gap-0.5">
				<h2 class="text-sm font-semibold">{nameOf(offer.url)} needs a browser</h2>
				<p class="text-muted-foreground text-xs">
					That host blocked automated access with a browser challenge. Fetching it in a real
					browser works, and saves it straight into the project folder.
				</p>
			</div>
		</div>

		<!-- The URL in full, wrapping and selectable: it is what the user is agreeing to open. -->
		<div
			class="neu-well max-h-24 overflow-y-auto rounded-lg p-2.5 font-mono text-xs break-all select-text"
		>
			{offer.url}
		</div>

		{#if offer.harness}
			<p class="text-muted-foreground text-[11px]">
				For <span class="font-medium">{offer.harness}</span>
			</p>
		{/if}

		<div class="flex items-center gap-2">
			<button
				type="button"
				class="neu-btn text-muted-foreground hover:text-foreground ml-auto flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs disabled:opacity-50"
				disabled={taken.has(offer.id)}
				onclick={() => act(offer, false)}
			>
				<XIcon class="size-3.5" />
				Not now
			</button>
			<button
				type="button"
				class="neu-btn flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs text-[var(--neu-accent)] disabled:opacity-50"
				disabled={taken.has(offer.id)}
				onclick={() => act(offer, true)}
			>
				<DownloadIcon class="size-3.5" />
				Fetch in browser
			</button>
		</div>
	</div>
{/each}
