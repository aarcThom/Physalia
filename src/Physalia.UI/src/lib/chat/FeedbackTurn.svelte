<script lang="ts">
	// A feedback turn — a pipeline component's message to the model, not the human's — headed by the
	// component(s) that produced it and COLLAPSED by default. A geometry report or a validation dump
	// runs to hundreds of lines and is machine-to-machine traffic; the human wants to know which node
	// spoke, and to read it only when curious. So the header carries the node's Grasshopper icon and
	// its canvas nickname, and the body sits behind it.
	//
	// Several sources appear when the turn was aggregated (Merge Signal's join, the Feedback
	// Collector's batch) — the header then names every contributing node, in causal order.
	import * as Collapsible from '$lib/components/ui/collapsible';
	import type { UiSource } from '$lib/bridge';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import MessageSquareDotIcon from '@lucide/svelte/icons/message-square-dot';
	import type { Snippet } from 'svelte';

	interface Props {
		/** The components behind the turn; empty/absent falls back to a generic "Feedback" header. */
		sources?: UiSource[];
		/** The turn's body (images + text bubble), mounted only while expanded. */
		children: Snippet;
	}

	let { sources, children }: Props = $props();

	let open = $state(false);
	let items = $derived(sources ?? []);
</script>

<!-- items-end: header and body both hug the right edge, as every user-side turn does. -->
<Collapsible.Root bind:open class="flex w-full min-w-0 flex-col items-end gap-1">
	<!-- The header wears the same pink bubble palette as the body (app.css --neu-feedback*), so a
	     collapsed feedback turn still reads as one at a glance. -->
	<Collapsible.Trigger
		class="flex max-w-full min-w-0 items-center gap-2 rounded-lg bg-[var(--neu-feedback)] transition-colors hover:bg-[var(--neu-feedback-hover)] px-3 py-2 text-left text-sm text-[var(--neu-feedback-text)] [box-shadow:var(--neu-feedback-shadow)]"
	>
		{#each items as source, i (i)}
			{#if i > 0}
				<span class="shrink-0 opacity-50">·</span>
			{/if}
			{#if source.icon}
				<img src={source.icon} alt="" class="size-4 shrink-0" />
			{:else}
				<MessageSquareDotIcon class="size-4 shrink-0" />
			{/if}
			<span class="truncate font-medium">{source.name}</span>
		{:else}
			<MessageSquareDotIcon class="size-4 shrink-0" />
			<span class="truncate font-medium">Feedback</span>
		{/each}
		<ChevronDownIcon
			class="size-4 shrink-0 transition-transform {open ? 'rotate-180' : ''}"
		/>
	</Collapsible.Trigger>
	<Collapsible.Content
		class="data-[state=closed]:fade-out-0 data-[state=closed]:slide-out-to-top-2 data-[state=open]:slide-in-from-top-2 data-[state=closed]:animate-out data-[state=open]:animate-in w-full min-w-0 outline-none"
	>
		<div class="flex w-full min-w-0 flex-col items-end gap-2">
			{@render children()}
		</div>
	</Collapsible.Content>
</Collapsible.Root>
