<script lang="ts">
	// Shown when a provider is configured but no Conversation Log is wired to the Chat yet (a freshly
	// opened Chat). Offers three ways forward as Physalia pills: auto-place + wire a Conversation Log,
	// open the preset gallery, or open the provider setup screen.
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import Pill from '$lib/chat/Pill.svelte';

	interface Props {
		/** Places a Conversation Log on the canvas and wires it to the Chat (host action). */
		onconnectconversationlog: () => void;
		/** Opens the Add-preset page (same destination as the header menu's "Add preset"). */
		onpreset: () => void;
		/** Opens the LLM-provider setup screen. */
		onconfigure: () => void;
	}

	let { onconnectconversationlog, onpreset, onconfigure }: Props = $props();
</script>

<div class="mx-auto flex w-full max-w-xl flex-col items-center gap-6 px-4 py-6">
	<HappyFace />

	<div class="flex w-full flex-col gap-4">
		<Pill onclick={onconnectconversationlog} class="w-full justify-start py-3">
			Connect a Conversation Log for manual setup
		</Pill>

		<Pill onclick={onpreset} class="w-full justify-start py-3">
			Place predefined workflow
		</Pill>

		<Pill onclick={onconfigure} class="w-full justify-start py-3">
			Configure LLM providers
		</Pill>
	</div>
</div>
