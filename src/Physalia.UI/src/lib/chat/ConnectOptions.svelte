<script lang="ts">
	// The surface shown when there is no conversation to display. It covers two cases, and the `home`
	// prop is what tells them apart.
	//
	// On HOME it is the entry screen: the two ways to get a harness onto the canvas — load a
	// predefined one from Files/PRESETS, or drop an empty one — plus provider setup. Opening the
	// window places nothing on its own, so until one is picked the canvas stays clean, and both stay
	// on offer for good: a harness exchanges no dataflow with anything outside itself, so a document
	// can carry as many as the user wants. The same two actions live in the header menu, which is how
	// they are reached once a conversation is under way.
	//
	// Otherwise it is a placed Chat with no Conversation Log wired yet — an empty harness. There the
	// logo stands alone: the user has already chosen their harness and is inside it, so offering to
	// place another would answer a question they did not ask. The status line below the surface is
	// what tells them to wire a Conversation Log.
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import Pill from '$lib/chat/Pill.svelte';

	interface Props {
		/** Opens the predefined-harness gallery (same destination as the header menu's "Add preset"). */
		onpreset: () => void;
		/** Drops an empty harness — a Chat and nothing else — onto the canvas (host action). */
		onemptyharness: () => void;
		/** Opens the LLM-provider setup screen. */
		onconfigure: () => void;
		/** Opens the MCP connections page. */
		onconfiguremcp: () => void;
		/** True on the Home screen, which is the only place the options are offered. */
		home: boolean;
	}

	let { onpreset, onemptyharness, onconfigure, onconfiguremcp, home }: Props = $props();
</script>

<div class="mx-auto flex w-full max-w-xl flex-col items-center gap-6 px-4 py-6">
	<HappyFace />

	{#if home}
		<div class="flex w-full flex-col gap-4">
			<Pill onclick={onpreset} class="w-full justify-start py-3">Place predefined harness</Pill>

			<Pill onclick={onemptyharness} class="w-full justify-start py-3">Place empty harness</Pill>

			<Pill onclick={onconfigure} class="w-full justify-start py-3">Configure LLM providers</Pill>

			<Pill onclick={onconfiguremcp} class="w-full justify-start py-3">
				Configure MCP connections
			</Pill>
		</div>
	{/if}
</div>
