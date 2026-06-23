<script lang="ts">
	// "Add new manual definition" page — placeholder for now. The flow it will host (manually
	// defining a pipeline / loading a custom .ghjson) is not built yet; this is a blank page
	// reachable from the header menu so the entry point exists.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import HappyFace from '$lib/chat/HappyFace.svelte';

	interface Props {
		/** Returns to the chat view. */
		onclose: () => void;
	}

	let { onclose }: Props = $props();
</script>

<div class="mx-auto flex w-full max-w-xl flex-col px-4 py-6">
	<div class="mb-4 flex items-center justify-between">
		<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={onclose}>
			<ArrowLeftIcon class="size-4" />
			Back to chat
		</Button>
	</div>

	<h2 class="text-lg font-semibold">Add a new manual definition</h2>

	<div class="mt-6 flex flex-col items-center gap-4">
		<HappyFace />
		<p class="text-muted-foreground text-center text-sm">Coming soon.</p>
	</div>
</div>
