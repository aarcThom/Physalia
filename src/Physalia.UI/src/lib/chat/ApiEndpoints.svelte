<script lang="ts">
	// "Configure API calls" page. Lists the HTTP APIs the model may read from, and edits them one
	// entry at a time through the bridge.
	//
	// Two things are deliberately NOT on this page.
	//
	// The KEY is never displayed. The host pushes `hasKey` and `keySource` and nothing else, so a
	// blank key box means "leave whatever is stored alone" rather than "clear it" — clearing is its
	// own button, worded as forgetting, the same way Disconnect works on the provider setup page.
	// Redisplaying a secret so a form can save it back unchanged buys nothing and puts it in the
	// page's memory.
	//
	// The CATALOG — what datasets the API holds, what the fields are called — is not here either. It
	// belongs on the API Call node's Description input, because that travels inside a preset and this
	// file cannot: it is per-user and per-machine. A pipeline shared without it arrives with its
	// wiring and none of its knowledge.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import TrashIcon from '@lucide/svelte/icons/trash-2';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import KeyIcon from '@lucide/svelte/icons/key';
	import GlobeIcon from '@lucide/svelte/icons/globe';
	import LoaderIcon from '@lucide/svelte/icons/loader-circle';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import type { ApiEndpointPayload, ApiResult, UiApiEndpoint } from '$lib/bridge';

	interface Props {
		/** APIs read from the store. Never carries a key — see the note above. */
		endpoints: UiApiEndpoint[];
		/** Outcome of the last save/delete/test, or null. */
		result: ApiResult | null;
		onsave: (entry: ApiEndpointPayload) => void;
		ondelete: (name: string) => void;
		/** Forgets a stored key without removing the endpoint. */
		onforgetkey: (name: string) => void;
		/** Requests the base URL as currently typed, writing nothing — proves the endpoint answers. */
		ontest: (entry: ApiEndpointPayload) => void;
		onclose: () => void;
	}

	let { endpoints, result, onsave, ondelete, onforgetkey, ontest, onclose }: Props = $props();

	type Draft = ApiEndpointPayload;

	// The entry being added or edited, or null while the list is showing.
	let draft = $state<Draft | null>(null);

	// Two-step delete, so a mis-click on a small icon does not lose an entry.
	let confirmingDelete = $state<string | null>(null);

	let testing = $state(false);

	// The host always answers a test on setApiResult, success or failure, so the spinner is cleared
	// by the reply arriving rather than by a timer.
	$effect(() => {
		if (result) {
			testing = false;
		}
	});

	function blank(): Draft {
		return {
			name: '',
			baseUrl: '',
			auth: 'none',
			authName: '',
			authPrefix: '',
			envVar: '',
			paging: 'none',
			key: '',
			replacing: ''
		};
	}

	function startAdd() {
		confirmingDelete = null;
		draft = blank();
	}

	function startEdit(endpoint: UiApiEndpoint) {
		confirmingDelete = null;
		draft = {
			name: endpoint.name,
			baseUrl: endpoint.baseUrl,
			auth: endpoint.auth,
			authName: endpoint.authName,
			authPrefix: endpoint.authPrefix,
			envVar: endpoint.envVar,
			paging: endpoint.paging ?? 'none',
			key: '',
			replacing: endpoint.name
		};
	}

	function cancel() {
		draft = null;
		testing = false;
	}

	function confirmDelete(name: string) {
		if (confirmingDelete === name) {
			confirmingDelete = null;
			ondelete(name);
			return;
		}
		confirmingDelete = name;
	}

	function save() {
		if (!draft || !canSave) {
			return;
		}
		onsave({ ...draft, name: draft.name.trim(), baseUrl: draft.baseUrl.trim() });
		draft = null;
	}

	function test() {
		if (!draft || !canSave) {
			return;
		}
		testing = true;
		ontest({ ...draft, name: draft.name.trim(), baseUrl: draft.baseUrl.trim() });
	}

	// A name and an http(s) base URL are the whole requirement. A key is not: an open-data portal
	// needs none, and that is the ordinary case rather than the exception.
	let canSave = $derived(
		!!draft && draft.name.trim().length > 0 && /^https?:\/\/\S+$/i.test(draft.baseUrl.trim())
	);

	// Which extra field the chosen auth form needs. Bearer needs none — it is Authorization plus a
	// fixed prefix, which is why it is offered separately from the general custom-header form.
	let needsAuthName = $derived(draft?.auth === 'customHeader' || draft?.auth === 'queryParameter');

	function describeKey(endpoint: UiApiEndpoint): string {
		if (endpoint.auth === 'none') {
			return 'No key needed';
		}
		if (!endpoint.hasKey) {
			return 'No key set';
		}
		return endpoint.keySource === 'stored' ? 'Key stored' : `Key from ${endpoint.keySource}`;
	}

	const FIELD =
		'neu-well text-foreground w-full rounded-md px-3 py-2 text-sm outline-none placeholder:text-muted-foreground/60';
</script>

<div class="mx-auto flex w-full max-w-xl flex-col px-4 py-6">
	<div class="mb-4 flex items-center justify-end">
		<Button variant="outline" size="sm" class="gap-1" onclick={onclose}>
			<ArrowLeftIcon class="size-4" />
			Go Back
		</Button>
	</div>

	<h2 class="text-lg font-semibold">API calls</h2>
	<p class="text-muted-foreground mt-1 text-sm">
		An API lets the model read live data — an open-data portal, a project database, anything that
		answers over HTTP. Add one here, then place an <em>API Call</em> component in a harness and pick
		it by name. Describe what the API holds on that component, so the description travels with your
		pipeline.
	</p>

	{#if result}
		<div
			class="mt-4 rounded-md px-3 py-2 text-sm {result.ok
				? 'bg-green-600/10 text-green-700'
				: 'bg-red-600/10 text-red-700'}"
		>
			{result.message}
		</div>
	{/if}

	{#if draft}
		<!-- ------------------------------------------------------------------ the add / edit form -->
		<div class="neu-raised mt-5 flex flex-col gap-4 rounded-md p-4">
			<div class="flex flex-col gap-1.5">
				<label class="text-xs font-medium" for="api-name">Name</label>
				<input
					id="api-name"
					class={FIELD}
					bind:value={draft.name}
					spellcheck="false"
					placeholder="vancouver"
				/>
				<p class="text-muted-foreground text-xs">
					How you will pick this API on the component, and part of the tool name the model sees.
				</p>
			</div>

			<div class="flex flex-col gap-1.5">
				<label class="text-xs font-medium" for="api-url">Base URL</label>
				<input
					id="api-url"
					class={FIELD}
					bind:value={draft.baseUrl}
					spellcheck="false"
					placeholder="https://opendata.vancouver.ca/api/explore/v2.1/"
				/>
				<p class="text-muted-foreground text-xs">
					Every call is made beneath this address, and the model cannot reach past it.
				</p>
			</div>

			<div class="flex flex-col gap-1.5">
				<label class="text-xs font-medium" for="api-paging">Paging</label>
				<select id="api-paging" class={FIELD} bind:value={draft.paging}>
					<option value="none">None — one response per call</option>
					<option value="limitOffset">limit / offset</option>
				</select>
				<p class="text-muted-foreground text-xs">
					Only set this if the API really pages this way. Told the wrong style, an API returns its
					first page over and over instead of failing, so leaving it off is always safe.
				</p>
			</div>

			<div class="flex flex-col gap-1.5">
				<label class="text-xs font-medium" for="api-auth">Authentication</label>
				<select id="api-auth" class={FIELD} bind:value={draft.auth}>
					<option value="none">None — the API is open</option>
					<option value="bearerHeader">Bearer token</option>
					<option value="customHeader">A named header</option>
					<option value="queryParameter">A query parameter</option>
				</select>
			</div>

			{#if needsAuthName}
				<div class="flex gap-2">
					<div class="flex flex-1 flex-col gap-1.5">
						<label class="text-xs font-medium" for="api-auth-name">
							{draft.auth === 'queryParameter' ? 'Parameter name' : 'Header name'}
						</label>
						<input
							id="api-auth-name"
							class={FIELD}
							bind:value={draft.authName}
							spellcheck="false"
							placeholder={draft.auth === 'queryParameter' ? 'apikey' : 'Authorization'}
						/>
					</div>
					{#if draft.auth === 'customHeader'}
						<div class="flex flex-1 flex-col gap-1.5">
							<label class="text-xs font-medium" for="api-auth-prefix">
								Value prefix <span class="text-muted-foreground font-normal">(optional)</span>
							</label>
							<input
								id="api-auth-prefix"
								class={FIELD}
								bind:value={draft.authPrefix}
								spellcheck="false"
								placeholder="Apikey "
							/>
						</div>
					{/if}
				</div>
			{/if}

			{#if draft.auth !== 'none'}
				<div class="flex flex-col gap-1.5">
					<label class="text-xs font-medium" for="api-key">
						API key
						{#if draft.replacing}
							<span class="text-muted-foreground font-normal">
								(leave blank to keep the one already saved)
							</span>
						{/if}
					</label>
					<input
						id="api-key"
						class={FIELD}
						type="password"
						bind:value={draft.key}
						spellcheck="false"
						autocomplete="off"
					/>
				</div>

				<div class="flex flex-col gap-1.5">
					<label class="text-xs font-medium" for="api-env">
						Environment variable <span class="text-muted-foreground font-normal">(optional)</span>
					</label>
					<input
						id="api-env"
						class={FIELD}
						bind:value={draft.envVar}
						spellcheck="false"
						placeholder="VANCOUVER_API_KEY"
					/>
					<p class="text-muted-foreground text-xs">
						Checked before the saved key. Naming one keeps the secret off this machine's disk
						entirely.
					</p>
				</div>
			{/if}

			<div class="flex flex-wrap items-center justify-end gap-3">
				<Button variant="outline" size="sm" disabled={!canSave || testing} onclick={test}>
					{#if testing}
						<LoaderIcon class="size-3.5 animate-spin" />
						Testing…
					{:else}
						Test
					{/if}
				</Button>
				<Button size="sm" disabled={!canSave} onclick={save}>Save</Button>
				<Button variant="ghost" size="sm" onclick={cancel}>Cancel</Button>
			</div>
		</div>
	{:else}
		<!-- ---------------------------------------------------------------------------- the list -->
		{#if endpoints.length > 0}
			<div class="mt-5 flex flex-col gap-2">
				{#each endpoints as endpoint (endpoint.name)}
					<div class="neu-raised flex items-start gap-3 rounded-md p-3">
						<GlobeIcon class="text-muted-foreground mt-0.5 size-4 shrink-0" />

						<div class="min-w-0 flex-1">
							<p class="truncate text-sm font-medium">{endpoint.name}</p>
							<p class="text-muted-foreground truncate text-xs">{endpoint.baseUrl}</p>
							<p class="text-muted-foreground mt-0.5 text-xs">
								{describeKey(endpoint)}{endpoint.paging === 'limitOffset' ? ' · pages' : ''}
							</p>
						</div>

						<div class="flex shrink-0 gap-1">
							{#if endpoint.hasKey && endpoint.keySource === 'stored'}
								<Button
									variant="ghost"
									size="sm"
									class="size-8 p-0"
									title="Forget the saved key"
									onclick={() => onforgetkey(endpoint.name)}
								>
									<KeyIcon class="size-3.5" />
								</Button>
							{/if}
							<Button
								variant="ghost"
								size="sm"
								class="size-8 p-0"
								title="Edit"
								onclick={() => startEdit(endpoint)}
							>
								<PencilIcon class="size-3.5" />
							</Button>
							<Button
								variant="ghost"
								size="sm"
								class="size-8 p-0 {confirmingDelete === endpoint.name ? 'text-red-600' : ''}"
								title={confirmingDelete === endpoint.name ? 'Click again to remove' : 'Remove'}
								onclick={() => confirmDelete(endpoint.name)}
							>
								<TrashIcon class="size-3.5" />
							</Button>
						</div>
					</div>
				{/each}
			</div>
		{:else}
			<div class="mt-6 flex flex-col items-center gap-4">
				<HappyFace class="size-16" />
				<p class="text-muted-foreground text-center text-sm">No APIs set up yet.</p>
			</div>
		{/if}

		<div class="mt-5 flex justify-center">
			<Button variant="outline" size="sm" class="gap-1" onclick={startAdd}>
				<PlusIcon class="size-4" />
				Add an API
			</Button>
		</div>
	{/if}
</div>
