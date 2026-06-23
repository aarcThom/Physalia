<script lang="ts">
	// First-run / add-a-provider setup screen. Shows a provider picker; selecting a provider opens
	// its setup guide with live links (opened in the system browser via the bridge). Key providers
	// are configured by pasting the key into the prompt box below (the host saves it); CLI / local
	// providers are detected automatically. Reached when no provider is configured, or opened
	// manually from the header dropdown to add another.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import ExternalLinkIcon from '@lucide/svelte/icons/external-link';
	import { PROVIDERS, getProvider } from '$lib/chat/providers';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import Pill from '$lib/chat/Pill.svelte';
	import type { SetupResult } from '$lib/bridge';

	interface Props {
		/** Currently opened provider guide, or null for the picker grid. */
		selectedId: string | null;
		/** Outcome of the most recent save-key attempt (cleared on navigation). */
		setupResult: SetupResult | null;
		/** True when setup was opened manually and a configured pipeline exists to return to. */
		canClose: boolean;
		/** Setup ids of providers already configured; shown as ready-state pills. */
		configuredProviders: string[];
		onselect: (id: string | null) => void;
		onopenlink: (url: string) => void;
		onclose: () => void;
	}

	let { selectedId, setupResult, canClose, configuredProviders, onselect, onopenlink, onclose }: Props =
		$props();

	let selected = $derived(getProvider(selectedId) ?? null);
	// Only show a result that belongs to the provider currently on screen.
	let result = $derived(
		selected && setupResult && setupResult.provider === selected.id ? setupResult : null
	);

	// Split the known providers into the already-configured (shown as ready pills) and the rest
	// (still clickable for their setup guide), keeping providers.ts order for a stable display.
	let configured = $derived(PROVIDERS.filter((p) => configuredProviders.includes(p.id)));
	let available = $derived(PROVIDERS.filter((p) => !configuredProviders.includes(p.id)));
</script>

<div class="mx-auto flex w-full max-w-xl flex-col px-4 py-6">
	{#if selected}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={() => onselect(null)}>
				<ArrowLeftIcon class="size-4" />
				All providers
			</Button>
			{#if canClose}
				<Button variant="ghost" size="sm" onclick={onclose}>Back to chat</Button>
			{/if}
		</div>

		<h2 class="text-lg font-semibold">{selected.label}</h2>
		<p class="text-muted-foreground mt-1 text-sm">{selected.blurb}</p>

		<ol class="mt-4 flex flex-col gap-2 text-sm">
			{#each selected.steps as step, i (i)}
				<li class="flex gap-2">
					<span
						class="neu-raised-sm text-muted-foreground mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full text-xs font-medium"
					>
						{i + 1}
					</span>
					<span class="leading-relaxed">{step}</span>
				</li>
			{/each}
		</ol>

		{#if selected.commands?.length}
			<div class="mt-4 flex flex-col gap-2">
				{#each selected.commands as command, i (i)}
					<div>
						{#if command.label}
							<p class="text-muted-foreground mb-1 text-xs">{command.label}</p>
						{/if}
						<pre
							class="neu-well overflow-x-auto rounded-md px-3 py-2 font-mono text-xs select-all">{command.code}</pre>
					</div>
				{/each}
			</div>
		{/if}

		<div class="mt-4 flex flex-wrap gap-2">
			{#each selected.links as link (link.url)}
				<Button variant="outline" size="sm" class="gap-1.5" onclick={() => onopenlink(link.url)}>
					<ExternalLinkIcon class="size-3.5" />
					{link.label}
				</Button>
			{/each}
		</div>

		{#if result}
			<div
				class="mt-4 rounded-md px-3 py-2 text-sm {result.ok
					? 'bg-green-600/10 text-green-700'
					: 'bg-red-600/10 text-red-700'}"
			>
				{result.message}
			</div>
		{/if}

		{#if selected.needsKey}
			<p class="text-muted-foreground mt-4 text-sm">
				When you have your key, paste it into the message box below and press Enter.
			</p>
			{#if selected.note}
				<p class="text-muted-foreground mt-1 text-xs">{selected.note}</p>
			{/if}
		{:else if selected.note}
			<p class="text-muted-foreground mt-4 text-xs">{selected.note}</p>
		{/if}
	{:else}
		{#if canClose}
			<div class="mb-2 flex justify-end">
				<Button variant="ghost" size="sm" onclick={onclose}>Back to chat</Button>
			</div>
		{/if}

		<div class="flex flex-col items-center gap-6">
			<HappyFace />

			{#if configured.length > 0}
				<div class="neu-raised w-full rounded-md p-4 text-sm leading-relaxed">
					You have already set up the following providers. You're good to go!
				</div>

				<!-- Ready providers: non-clickable Physalia pills (see Pill.svelte for the style). -->
				<div class="flex w-full flex-wrap gap-3">
					{#each configured as provider (provider.id)}
						<Pill>{provider.label}</Pill>
					{/each}
				</div>

				<div class="neu-raised w-full rounded-md p-4 text-sm leading-relaxed">
					In addition, you can set up these providers. Click on the button for instructions.
				</div>
			{:else}
				<div class="neu-raised w-full rounded-md p-4 text-sm leading-relaxed">
					Welcome to Physalia. Let's get you set up. You haven't set up any LLM providers yet, so
					let's do that first. Choose what provider you want to use:
				</div>
			{/if}

			{#if available.length > 0}
				<div class="flex w-full flex-wrap gap-3">
					{#each available as provider (provider.id)}
						<Button variant="outline" class="h-auto py-2" onclick={() => onselect(provider.id)}>
							{provider.label}
						</Button>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>
