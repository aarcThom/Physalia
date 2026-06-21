<script lang="ts">
	// First-run setup screen, shown when the host reports no LLM provider is configured
	// (needsSetup). The user picks a provider; selecting one opens a placeholder page that will
	// later host real, per-provider setup instructions pulled from the Physalia website.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';

	interface Provider {
		id: string;
		label: string;
	}

	const providers: Provider[] = [
		{ id: 'claude-code', label: 'Claude Code (subscription)' },
		{ id: 'anthropic', label: 'Anthropic' },
		{ id: 'google', label: 'Google' },
		{ id: 'openai', label: 'OpenAI' },
		{ id: 'deepseek', label: 'Deepseek' },
		{ id: 'local-llm', label: 'Local LLM' },
		{ id: 'openrouter', label: 'Open Router' },
		{ id: 'other', label: 'Other' }
	];

	let selected = $state<Provider | null>(null);
</script>

{#if selected}
	<div class="mx-auto flex h-full w-full max-w-xl flex-col gap-4 px-4 py-6">
		<Button variant="ghost" size="sm" class="-ml-2 self-start gap-1" onclick={() => (selected = null)}>
			<ArrowLeftIcon class="size-4" />
			Back
		</Button>
		<h2 class="text-lg font-semibold">{selected.label}</h2>
		<p class="text-muted-foreground text-sm">
			Setup instructions for {selected.label} are coming soon. This panel will walk you through
			connecting {selected.label} as your LLM provider.
		</p>
	</div>
{:else}
	<div class="mx-auto flex w-full max-w-xl flex-col items-center gap-6 px-4 py-6">
		<!-- Placeholder face — swapped for real artwork later. -->
		<svg
			width="120"
			height="120"
			viewBox="0 0 120 120"
			fill="none"
			xmlns="http://www.w3.org/2000/svg"
			aria-hidden="true"
		>
			<circle cx="60" cy="60" r="50" stroke="#dc2626" stroke-width="3" />
			<circle cx="42" cy="48" r="6" stroke="#dc2626" stroke-width="3" />
			<circle cx="78" cy="48" r="6" stroke="#dc2626" stroke-width="3" />
			<path
				d="M40 74 Q60 92 80 74"
				stroke="#dc2626"
				stroke-width="3"
				stroke-linecap="round"
				fill="none"
			/>
		</svg>

		<div class="w-full rounded-md border p-4 text-sm leading-relaxed">
			Welcome to Physalia. Let's get you set up. You haven't set up any LLM providers yet, so
			let's do that first. Choose what provider you want to use:
		</div>

		<div class="flex w-full flex-wrap gap-3">
			{#each providers as provider (provider.id)}
				<Button variant="outline" class="h-auto py-2" onclick={() => (selected = provider)}>
					{provider.label}
				</Button>
			{/each}
		</div>
	</div>
{/if}
