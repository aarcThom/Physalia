<script lang="ts">
	// Shown when a provider is configured but the Chat has no Conversation Log wired yet (a freshly
	// opened Chat). Offers the two ways to get a harness onto the canvas — load a predefined one from
	// Files/PRESETS, or drop an empty one — plus the provider setup screen.
	//
	// Opening the window places nothing on its own, so until one of these is picked the canvas stays
	// clean. Once a harness exists the two placement pills drop away: a second one would orphan the
	// first, and the remaining work happens inside the harness on the canvas.
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import Pill from '$lib/chat/Pill.svelte';

	interface Props {
		/** Opens the predefined-harness gallery (same destination as the header menu's "Add preset"). */
		onpreset: () => void;
		/** Drops an empty harness — just the Chat — onto the canvas (host action). */
		onemptyharness: () => void;
		/** Opens the LLM-provider setup screen. */
		onconfigure: () => void;
		/** True once this Chat sits in a harness, which retires the placement options. */
		harnessPlaced: boolean;
	}

	let { onpreset, onemptyharness, onconfigure, harnessPlaced }: Props = $props();
</script>

<div class="mx-auto flex w-full max-w-xl flex-col items-center gap-6 px-4 py-6">
	<HappyFace />

	<div class="flex w-full flex-col gap-4">
		{#if !harnessPlaced}
			<Pill onclick={onpreset} class="w-full justify-start py-3">Place predefined harness</Pill>

			<Pill onclick={onemptyharness} class="w-full justify-start py-3">Place empty harness</Pill>
		{:else}
			<p class="text-muted-foreground text-center text-sm">
				Your harness is on the canvas. Right-click it and choose <em>Edit Harness</em> to go inside,
				then wire a Conversation Log to the Chat.
			</p>
		{/if}

		<Pill onclick={onconfigure} class="w-full justify-start py-3">Configure LLM providers</Pill>
	</div>
</div>
