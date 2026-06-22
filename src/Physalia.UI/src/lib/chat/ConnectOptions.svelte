<script lang="ts">
	// Shown when a provider is configured but no Recorder is wired to the Chatbox yet (a freshly
	// opened Chatbox). Offers three ways forward as Physalia pills: auto-place + wire a Recorder,
	// drop a predefined workflow (not built yet — placeholder), or open the provider setup screen.
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import Pill from '$lib/chat/Pill.svelte';

	interface Props {
		/** Places a Recorder on the canvas and wires it to the Chatbox (host action). */
		onconnectrecorder: () => void;
		/** Opens the LLM-provider setup screen. */
		onconfigure: () => void;
	}

	let { onconnectrecorder, onconfigure }: Props = $props();

	// Predefined workflows aren't implemented yet; the button reveals a placeholder note for now.
	let showWorkflowPlaceholder = $state(false);
</script>

<div class="mx-auto flex w-full max-w-xl flex-col items-center gap-6 px-4 py-6">
	<HappyFace />

	<div class="flex w-full flex-col gap-4">
		<Pill onclick={onconnectrecorder} class="w-full justify-start py-3">
			Connect a recorder for manual setup
		</Pill>

		<Pill onclick={() => (showWorkflowPlaceholder = true)} class="w-full justify-start py-3">
			Place predefined workflow
		</Pill>

		<Pill onclick={onconfigure} class="w-full justify-start py-3">
			Configure LLM providers
		</Pill>

		{#if showWorkflowPlaceholder}
			<p class="text-muted-foreground text-xs">
				Predefined workflows are coming soon — this will open a guided workflow window.
			</p>
		{/if}
	</div>
</div>
