<script lang="ts">
	// Shown when a provider is configured but no Recorder is wired to the Chatbox yet (a freshly
	// opened Chatbox). Offers three ways forward as Physalia pills: auto-place + wire a Recorder,
	// open the preset gallery, or open the provider setup screen.
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import Pill from '$lib/chat/Pill.svelte';

	interface Props {
		/** Places a Recorder on the canvas and wires it to the Chatbox (host action). */
		onconnectrecorder: () => void;
		/** Opens the Add-preset page (same destination as the header menu's "Add preset"). */
		onpreset: () => void;
		/** Opens the LLM-provider setup screen. */
		onconfigure: () => void;
	}

	let { onconnectrecorder, onpreset, onconfigure }: Props = $props();
</script>

<div class="mx-auto flex w-full max-w-xl flex-col items-center gap-6 px-4 py-6">
	<HappyFace />

	<div class="flex w-full flex-col gap-4">
		<Pill onclick={onconnectrecorder} class="w-full justify-start py-3">
			Connect a recorder for manual setup
		</Pill>

		<Pill onclick={onpreset} class="w-full justify-start py-3">
			Place predefined workflow
		</Pill>

		<Pill onclick={onconfigure} class="w-full justify-start py-3">
			Configure LLM providers
		</Pill>
	</div>
</div>
