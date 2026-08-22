<script lang="ts">
	// The Physalia "pill": a light-blue panel with a faded pink edge, echoing the Prompter
	// component's look (convo fill #daf3f5, hilight #e8bcff, text #2f0857). This is the single
	// source of truth for the pill style — change it here and every pill (configured-provider
	// labels, the connect-page options, …) updates. Renders as a <button> when `onclick` is
	// given (interactive, with hover/active feedback) or a static <span> otherwise.
	import type { Snippet } from 'svelte';
	import { cn } from '$lib/utils';

	interface Props {
		/** When set, the pill is an interactive button; otherwise a non-clickable label. */
		onclick?: (() => void) | null;
		/** Extra classes merged over the base style (e.g. `w-full text-left`). */
		class?: string;
		children: Snippet;
	}

	let { onclick = null, class: className = '', children }: Props = $props();

	const base =
		'neu-raised inline-flex items-center rounded-md px-3 py-1.5 text-sm font-medium text-[#2f0857]';
	const interactive =
		'cursor-pointer transition hover:bg-[var(--neu-hover)] active:[box-shadow:var(--neu-inset)]';
</script>

{#if onclick}
	<button type="button" {onclick} class={cn(base, interactive, className)}>
		{@render children()}
	</button>
{:else}
	<span class={cn(base, 'select-none', className)}>
		{@render children()}
	</span>
{/if}
