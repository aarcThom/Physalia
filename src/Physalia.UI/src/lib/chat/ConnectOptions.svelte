<script lang="ts">
	// Shown when a provider is configured but the Chat has no Conversation Log wired yet (a freshly
	// opened Chat). Offers the two ways to get a harness onto the canvas — load a predefined one from
	// Files/PRESETS, or drop an empty one — plus the provider setup screen.
	//
	// Opening the window places nothing on its own, so until one of these is picked the canvas stays
	// clean. Both stay on offer afterwards: a harness exchanges no dataflow with anything outside
	// itself, so a document can carry as many as the user wants, each its own line of work. The same
	// two actions live in the header menu, which is how they are reached once a conversation is under
	// way and this screen has given way to the transcript.
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import Pill from '$lib/chat/Pill.svelte';

	interface Props {
		/** Opens the predefined-harness gallery (same destination as the header menu's "Add preset"). */
		onpreset: () => void;
		/** Drops an empty harness — a Chat and nothing else — onto the canvas (host action). */
		onemptyharness: () => void;
		/** Opens the LLM-provider setup screen. */
		onconfigure: () => void;
	}

	let { onpreset, onemptyharness, onconfigure }: Props = $props();
</script>

<div class="mx-auto flex w-full max-w-xl flex-col items-center gap-6 px-4 py-6">
	<HappyFace />

	<div class="flex w-full flex-col gap-4">
		<Pill onclick={onpreset} class="w-full justify-start py-3">Place predefined harness</Pill>

		<Pill onclick={onemptyharness} class="w-full justify-start py-3">Place empty harness</Pill>

		<Pill onclick={onconfigure} class="w-full justify-start py-3">Configure LLM providers</Pill>
	</div>
</div>
