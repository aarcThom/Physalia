<script lang="ts">
	// First-run / add-a-provider setup screen. Shows a provider picker; selecting a provider opens
	// its guide and ONE footer, chosen by that provider's status:
	//
	//   connected            -> a note plus Disconnect
	//   available, not yet   -> ONE button ("Add to Physalia" / "Connect Claude Code")
	//   nothing found        -> the API URL + key form, or a Detect button for probed providers
	//
	// The middle state is the point: a key exported for another tool, or a CLI installed for another
	// purpose, is AVAILABLE without having been chosen. Physalia offers it; it never adopts it.
	//
	// Once a provider IS available the setup instructions are dropped entirely — see the markup.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import ExternalLinkIcon from '@lucide/svelte/icons/external-link';
	import { PROVIDERS, getProvider } from '$lib/chat/providers';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import Pill from '$lib/chat/Pill.svelte';
	import type { ProviderStatus, SetupResult } from '$lib/bridge';

	interface Props {
		/** Currently opened provider guide, or null for the picker grid. */
		selectedId: string | null;
		/** Outcome of the most recent save / connect / detect attempt (cleared on navigation). */
		setupResult: SetupResult | null;
		/** True when setup was opened manually and a configured pipeline exists to return to. */
		canClose: boolean;
		/** Setup ids of providers already configured (available AND connected); shown as ready pills. */
		configuredProviders: string[];
		/** Per-provider availability + opt-in, from the host. */
		providerStatuses: ProviderStatus[];
		onselect: (id: string | null) => void;
		onopenlink: (url: string) => void;
		onclose: () => void;
		/** Save one provider's endpoint and key. Either may be empty where the provider allows it. */
		onsaveprovider: (id: string, url: string, key: string) => void;
		/** Run the availability check for a probed provider (CLI on PATH, local server answering). */
		ondetect: (id: string) => void;
		/** Opt a provider in, once it is available. */
		onconnect: (id: string) => void;
		/** Opt back out, forgetting any stored key. */
		ondisconnect: (id: string) => void;
	}

	let {
		selectedId,
		setupResult,
		canClose,
		configuredProviders,
		providerStatuses,
		onselect,
		onopenlink,
		onclose,
		onsaveprovider,
		ondetect,
		onconnect,
		ondisconnect
	}: Props = $props();

	let selected = $derived(getProvider(selectedId) ?? null);
	let status = $derived(providerStatuses.find((p) => p.id === selectedId) ?? null);
	let isAvailable = $derived(!!status && status.source !== 'none');
	let isConnected = $derived(!!status?.activated && isAvailable);

	// What the single opt-in button says. The variable name lives in the found-line above it, so this
	// stays a plain verb rather than a sentence on a button.
	let connectLabel = $derived.by(() => {
		if (!selected || !status) return 'Add to Physalia';
		if (status.source === 'detected') return `Connect ${selected.label.replace(/ \(.*\)$/, '')}`;
		return 'Add to Physalia';
	});

	// The single line shown in place of the setup instructions once a provider is already available.
	let foundNote = $derived.by(() => {
		if (!selected || !status) return '';
		const name = selected.label.replace(/ \(.*\)$/, '');
		if (status.source === 'detected') return `${name} found on your machine.`;
		if (status.source === 'environment') {
			return status.detail
				? `A ${name} key was found in your local environment (${status.detail}).`
				: `A ${name} key was found in your local environment.`;
		}
		return `A ${name} key is saved on this machine.`;
	});

	// Form state. Re-seeded whenever the open provider changes — $derived would fight the user's
	// typing, so this is an explicit effect keyed on the id.
	let url = $state('');
	let key = $state('');
	let pending = $state(false);

	$effect(() => {
		const id = selectedId;
		const provider = getProvider(id);
		url = provider?.defaultUrl ?? '';
		key = '';
		pending = false;
	});

	// A result arriving is the end of the round trip, whatever it says.
	$effect(() => {
		if (setupResult) {
			pending = false;
		}
	});

	// "other" has no default endpoint to fall back on, so it is the one provider that cannot be
	// saved on a key alone.
	let canSave = $derived(
		!!selected &&
			(key.trim().length > 0 || url.trim().length > 0) &&
			(!selected.needsUrl || url.trim().length > 0 || (selected.defaultUrl ?? '').length > 0)
	);

	function save() {
		if (!selected || !canSave) return;
		pending = true;
		onsaveprovider(selected.id, url.trim(), key.trim());
	}

	function detect() {
		if (!selected) return;
		pending = true;
		ondetect(selected.id);
	}

	function connect() {
		if (!selected) return;
		pending = true;
		onconnect(selected.id);
	}

	function disconnect() {
		if (!selected) return;
		pending = true;
		ondisconnect(selected.id);
	}

	// Matches the MCP setup page's field styling so the two setup surfaces read as one thing.
	const FIELD =
		'neu-well text-foreground w-full rounded-md px-3 py-2 text-sm outline-none placeholder:text-muted-foreground/60';

	// Only show a result that belongs to the provider currently on screen.
	let result = $derived(
		selected && setupResult && setupResult.provider === selected.id ? setupResult : null
	);

	// Split the known providers into the already-configured (shown as ready pills, LLM + tool alike)
	// and the rest (still clickable for their setup guide), keeping providers.ts order. The
	// unconfigured ones split again into chat-model providers and web-tool keys (Tavily / Jina),
	// which get their own section so they don't look like a required LLM choice.
	let configured = $derived(PROVIDERS.filter((p) => configuredProviders.includes(p.id)));
	let unconfigured = $derived(PROVIDERS.filter((p) => !configuredProviders.includes(p.id)));
	let availableLlm = $derived(unconfigured.filter((p) => p.kind !== 'tool'));
	let availableTools = $derived(unconfigured.filter((p) => p.kind === 'tool'));
</script>

<div class="mx-auto flex w-full max-w-xl flex-col px-4 py-4 sm:py-6">
	{#if selected}
		<!-- ONE back control per page, and it always steps back exactly one level: from a provider
		     back to the list, and from the list back to the chat. This row used to carry both
		     ("All providers" and "Go Back"), which read as the same button twice — and the second
		     one skipped a level, so the way back to the list disappeared during first-run setup,
		     where it is not drawn at all. -->
		<div class="mb-4 flex items-center justify-end">
			<Button variant="outline" size="sm" class="gap-1" onclick={() => onselect(null)}>
				<ArrowLeftIcon class="size-4" />
				Go Back
			</Button>
		</div>

		<h2 class="text-lg font-semibold">{selected.label}</h2>

		{#if isAvailable}
			<!-- Already set up on this machine. The blurb, the numbered install steps, the install
			     commands and the console links all exist to get someone TO this point — showing them
			     to someone already past it is noise in front of the one control that matters. One
			     line saying what was found, and the footer below it, is the whole page. -->
			<p class="mt-1 text-sm">{foundNote}</p>
		{:else}
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
		{/if}

		{#if result}
			<div
				class="mt-4 rounded-md px-3 py-2 text-sm {result.ok
					? 'bg-green-600/10 text-green-700'
					: 'bg-red-600/10 text-red-700'}"
			>
				{result.message}
			</div>
		{/if}

		{#if isConnected}
			<!-- Already opted in. Disconnect also forgets any stored key, which is why it is not
			     worded as a mere toggle. -->
			<div class="mt-4 flex items-center gap-3">
				<Button variant="outline" onclick={disconnect} disabled={pending}>
					{pending ? 'Working…' : 'Disconnect'}
				</Button>
				<p class="text-muted-foreground text-xs">Connected to Physalia.</p>
			</div>
		{:else if isAvailable}
			<!-- Found, but not chosen. ONE button — this is the whole opt-in step. -->
			<div class="mt-4 flex flex-col gap-2">
				<div class="flex items-center gap-3">
					<Button onclick={connect} disabled={pending}>
						{pending ? 'Connecting…' : connectLabel}
					</Button>
				</div>
				<p class="text-muted-foreground text-xs">Physalia will not use it until you connect it.</p>
			</div>
		{:else if selected.detect}
			<!-- Probed and not present: install first, then check. -->
			<div class="mt-4 flex items-center gap-3">
				<Button onclick={detect} disabled={pending}>
					{pending ? 'Checking…' : selected.detect}
				</Button>
				{#if selected.note}
					<p class="text-muted-foreground text-xs">{selected.note}</p>
				{/if}
			</div>
		{:else if selected.needsKey}
			<div class="mt-4 flex flex-col gap-3">
				{#if selected.needsUrl}
					<div class="flex flex-col gap-1.5">
						<label class="text-xs font-medium" for="setup-url">API URL</label>
						<input
							id="setup-url"
							class={FIELD}
							bind:value={url}
							spellcheck="false"
							autocomplete="off"
							placeholder="https://…"
						/>
					</div>
				{/if}

				<div class="flex flex-col gap-1.5">
					<label class="text-xs font-medium" for="setup-key">API key</label>
					<input
						id="setup-key"
						type="password"
						class={FIELD}
						bind:value={key}
						spellcheck="false"
						autocomplete="off"
						placeholder={selected.id === 'other' ? 'optional' : 'paste your key'}
						onkeydown={(event: KeyboardEvent) => {
							if (event.key === 'Enter') {
								event.preventDefault();
								save();
							}
						}}
					/>
				</div>

				<div class="flex items-center gap-3">
					<Button onclick={save} disabled={pending || !canSave}>
						{pending ? 'Saving…' : 'Save'}
					</Button>
					{#if selected.note}
						<p class="text-muted-foreground text-xs">{selected.note}</p>
					{/if}
				</div>
			</div>
		{:else if selected.note}
			<p class="text-muted-foreground mt-4 text-xs">{selected.note}</p>
		{/if}
	{:else}
		{#if canClose}
			<!-- Top-right, like every other page's back control. Absent during first-run setup:
			     there is no chat to go back to yet. -->
			<div class="mb-2 flex items-center justify-end">
				<Button variant="outline" size="sm" class="gap-1" onclick={onclose}>
					<ArrowLeftIcon class="size-4" />
					Go Back
				</Button>
			</div>
		{/if}

		<div class="flex flex-col items-center gap-4 sm:gap-6">
			<!-- The mark is decorative and the message under it is not, so it yields first. At 120px
			     plus the surrounding gaps it filled the default 460x620 window on its own, leaving the
			     welcome text and every provider button below the fold — and a first-run screen you
			     have to scroll to find reads as an empty one. It shrinks in a short window and goes
			     entirely when there is no room for it at all. -->
			<div
				class="[&>svg]:size-[120px] [@media(max-height:760px)]:[&>svg]:size-[72px] [@media(max-height:560px)]:hidden"
			>
				<HappyFace />
			</div>

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

				{#if availableLlm.length > 0}
					<div class="neu-raised w-full rounded-md p-4 text-sm leading-relaxed">
						In addition, you can set up these providers. Click on the button for instructions.
					</div>
				{/if}
			{:else}
				<div class="neu-raised w-full rounded-md p-4 text-sm leading-relaxed">
					Welcome to Physalia. Let's get you set up. You haven't set up any LLM providers yet, so
					let's do that first. Choose what provider you want to use:
				</div>
			{/if}

			{#if availableLlm.length > 0}
				<div class="flex w-full flex-wrap gap-3">
					{#each availableLlm as provider (provider.id)}
						<Button variant="outline" class="h-auto py-2" onclick={() => onselect(provider.id)}>
							{provider.label}
						</Button>
					{/each}
				</div>
			{/if}

			{#if availableTools.length > 0}
				<!-- Web-tool keys (Tavily / Jina): optional, for the Web Search / Read URL tools. -->
				<div class="neu-raised w-full rounded-md p-4 text-sm leading-relaxed">
					To use the Web Search and Read URL tools, set up a free Tavily account. (Jina is
					optional — Read URL works without a key.) Click a button for instructions.
				</div>

				<div class="flex w-full flex-wrap gap-3">
					{#each availableTools as provider (provider.id)}
						<Button variant="outline" class="h-auto py-2" onclick={() => onselect(provider.id)}>
							{provider.label}
						</Button>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>
