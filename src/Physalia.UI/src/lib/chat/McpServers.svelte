<script lang="ts">
	// "Configure MCP connections" page. Lists what Files/MCP_SERVERS.YAML holds and edits it one entry
	// at a time through the bridge — the host rewrites only that entry's lines, so the file's own
	// commentary and ordering survive.
	//
	// The form has TWO modes, and that is not decoration: the standard `mcpServers` shape covers two
	// different transports. A `command` server is a subprocess (command + args + env + cwd) and is
	// what almost every published server is; a `url` one is an HTTP endpoint the Physalia bridge
	// relays to. Offering a single "URL + key" box would refuse nearly everything people actually
	// paste in. The modes are LABELLED after those two keys rather than local/remote, because the
	// distinction is the transport and not the machine — a URL server is very often on localhost.
	// The `transport: 'local' | 'remote'` values in the code and on the wire are unchanged, and so
	// are the YAML keys: a config pasted from any README must keep working verbatim.
	//
	// Nothing here resolves a ${VAR}: the host hands values over exactly as written and takes them back
	// the same way, so a token stays in the environment where the user put it.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import TrashIcon from '@lucide/svelte/icons/trash-2';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import ServerIcon from '@lucide/svelte/icons/server';
	import GlobeIcon from '@lucide/svelte/icons/globe';
	import LogInIcon from '@lucide/svelte/icons/log-in';
	import PlugIcon from '@lucide/svelte/icons/plug';
	import LoaderIcon from '@lucide/svelte/icons/loader-circle';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import type { McpResult, McpServerPayload, UiMcpServer } from '$lib/bridge';

	interface Props {
		/** Servers read from MCP_SERVERS.YAML, values verbatim (${VAR} intact). */
		servers: UiMcpServer[];
		/** Why the file cannot be written from here (JSON form, no wrapper), or null when it can. */
		readOnlyReason: string | null;
		/** Outcome of the last save/delete, or null. */
		result: McpResult | null;
		onsave: (entry: McpServerPayload) => void;
		ondelete: (name: string) => void;
		/** Connects to a saved server — for a remote one, this is what opens the browser sign-in. */
		onsignin: (name: string) => void;
		onclose: () => void;
	}

	let { servers, readOnlyReason, result, onsave, ondelete, onsignin, onclose }: Props = $props();

	// Which server a connection attempt is running for, so its row can show a spinner. The host always
	// answers on setMcpResult (success or failure), and the effect below clears this when it does.
	let connecting = $state<string | null>(null);

	$effect(() => {
		if (result) {
			connecting = null;
		}
	});

	/** Editable pair rows. Kept as a list, not a map, so a half-typed key does not collapse a row. */
	interface PairRow {
		key: string;
		value: string;
	}

	/** The entry currently open in the form. `original` is null for a new server. */
	interface Draft {
		original: string | null;
		name: string;
		/** True once the human has typed in the name box, which stops the URL from suggesting one. */
		nameTouched: boolean;
		transport: 'local' | 'remote';
		command: string;
		args: string;
		cwd: string;
		env: PairRow[];
		url: string;
		headers: PairRow[];
		scope: string;
	}

	let draft = $state<Draft | null>(null);
	let confirmingDelete = $state<string | null>(null);

	function blankDraft(): Draft {
		return {
			original: null,
			name: '',
			nameTouched: false,
			transport: 'local',
			command: '',
			args: '',
			cwd: '',
			env: [{ key: '', value: '' }],
			url: '',
			headers: [{ key: '', value: '' }],
			scope: ''
		};
	}

	// One trailing blank row so there is always somewhere to type, without an "add row" button.
	function pairsOf(pairs: [string, string][]): PairRow[] {
		return [...pairs.map(([key, value]) => ({ key, value })), { key: '', value: '' }];
	}

	function draftOf(server: UiMcpServer): Draft {
		return {
			original: server.name,
			name: server.name,
			nameTouched: true,
			transport: server.transport,
			command: server.command,
			// One argument per line: an arg is often a long path, and a comma-separated box would
			// mangle any that contained a comma.
			args: server.args.join('\n'),
			cwd: server.cwd,
			env: pairsOf(server.env),
			url: server.url,
			headers: pairsOf(server.headers),
			scope: server.scope
		};
	}

	function startAdd() {
		draft = blankDraft();
		confirmingDelete = null;
	}

	function startEdit(server: UiMcpServer) {
		draft = draftOf(server);
		confirmingDelete = null;
	}

	function cancel() {
		draft = null;
	}

	// Keep exactly one empty row at the end: it grows when the last row is filled, and shrinks again
	// when rows are cleared. Growing only (the first cut) left a blank row behind for good the moment
	// anything was typed and deleted — two empty "Authorization" rows reading as a form demanding
	// something, which is the opposite of what an optional field should look like.
	function tidyPairs(rows: PairRow[]) {
		while (rows.length > 1) {
			const last = rows[rows.length - 1];
			const previous = rows[rows.length - 2];
			if (last.key || last.value || previous.key || previous.value) {
				break;
			}
			rows.pop();
		}

		const last = rows[rows.length - 1];
		if (!last || last.key || last.value) {
			rows.push({ key: '', value: '' });
		}
	}

	function cleanPairs(rows: PairRow[]): [string, string][] {
		return rows.filter((r) => r.key.trim()).map((r) => [r.key.trim(), r.value] as [string, string]);
	}

	// What is still missing, in the form's own reading order — shown next to the save button. A
	// disabled button with no reason is a dead end: the Name box is the first field and scrolls out of
	// sight on a narrow window, so "I can't submit" reads as being about whatever is still on screen,
	// which is the optional half.
	let missing = $derived.by(() => {
		if (!draft) {
			return [];
		}

		const gaps: string[] = [];
		if (!draft.name.trim()) {
			gaps.push('a name');
		}
		if (draft.transport === 'remote') {
			if (!draft.url.trim()) {
				gaps.push('a URL');
			}
		} else if (!draft.command.trim()) {
			gaps.push('a command');
		}

		return gaps;
	});

	let canSave = $derived(missing.length === 0);

	// A URL server given a header of its own already carries its credential, so connecting to it can
	// never open a browser: the SDK's OAuth path is reactive and fires only on a 401. Promising a
	// sign-in that will not happen is worse than saying nothing, so the button renames itself.
	let hasOwnCredential = $derived((draft?.headers ?? []).some((r) => r.key.trim().length > 0));

	// A remote server's name is nearly always its host, so offer that rather than making the user
	// invent one — but only until they type, and never over something they wrote. "mcp." prefixes are
	// dropped because a server called "mcp" says nothing.
	function suggestName(url: string): string {
		try {
			const host = new URL(url.trim()).hostname.replace(/^(www|mcp|api)\./i, '');
			return host.split('.')[0] ?? '';
		} catch {
			return '';
		}
	}

	function urlChanged() {
		if (draft && !draft.nameTouched && !draft.name.trim()) {
			draft.name = suggestName(draft.url);
		}
	}

	function save(signIn = false) {
		if (!draft || !canSave) {
			return;
		}

		const d = draft;
		if (signIn) {
			connecting = d.name.trim();
		}

		onsave({
			name: d.name.trim(),
			transport: d.transport,
			command: d.transport === 'local' ? d.command.trim() : '',
			args:
				d.transport === 'local'
					? d.args
							.split('\n')
							.map((a) => a.trim())
							.filter(Boolean)
					: [],
			cwd: d.transport === 'local' ? d.cwd.trim() : '',
			env: d.transport === 'local' ? cleanPairs(d.env) : [],
			url: d.transport === 'remote' ? d.url.trim() : '',
			headers: d.transport === 'remote' ? cleanPairs(d.headers) : [],
			scope: d.transport === 'remote' ? d.scope.trim() : '',
			// Only meaningful on a rename; the host edits that entry in place rather than adding a
			// second one beside the original.
			replacing: d.original ?? '',
			signIn
		});

		draft = null;
	}

	function signIn(name: string) {
		connecting = name;
		onsignin(name);
	}

	function confirmDelete(name: string) {
		if (confirmingDelete === name) {
			ondelete(name);
			confirmingDelete = null;
		} else {
			confirmingDelete = name;
		}
	}

	/** One-line summary under a server's name in the list. */
	function summarize(server: UiMcpServer): string {
		if (server.transport === 'remote') {
			return server.url;
		}
		return [server.command, ...server.args].join(' ');
	}

	const FIELD =
		'neu-well text-foreground w-full rounded-md px-3 py-2 text-sm outline-none placeholder:text-muted-foreground/60';
</script>

<div class="mx-auto flex w-full max-w-xl flex-col px-4 py-6">
	<div class="mb-4 flex items-center justify-between">
		<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={onclose}>
			<ArrowLeftIcon class="size-4" />
			Back to chat
		</Button>
	</div>

	<h2 class="text-lg font-semibold">MCP connections</h2>
	<p class="text-muted-foreground mt-1 text-sm">
		An MCP server lends its tools to the model. Add one here and it is written to
		<em>MCP_SERVERS.YAML</em>; then place an <em>MCP Server</em> component in a harness and pick it by
		name.
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

	{#if readOnlyReason}
		<div class="mt-4 rounded-md bg-amber-600/10 px-3 py-2 text-sm text-amber-700">
			{readOnlyReason}
		</div>
	{/if}

	{#if draft}
		<!-- ------------------------------------------------------------------ the add / edit form -->
		<div class="neu-raised mt-5 flex flex-col gap-4 rounded-md p-4">
			<div class="flex flex-col gap-1.5">
				<label class="text-xs font-medium" for="mcp-name">
					Name <span class="text-muted-foreground font-normal">(required)</span>
				</label>
				<input
					id="mcp-name"
					class={FIELD}
					bind:value={draft.name}
					oninput={() => draft && (draft.nameTouched = true)}
					placeholder="filesystem"
					spellcheck="false"
				/>
				<p class="text-muted-foreground text-xs">
					How you will pick this server on an MCP Server component. Its tools reach the model
					namespaced under it.
				</p>
			</div>

			<!-- The transport switch. Command is first and default because it is what nearly every
			     published server is. The two options are named after the field each one asks for —
			     which is also the YAML key it writes — because the real split is subprocess-vs-HTTP,
			     NOT this-machine-vs-elsewhere: a desktop app exposing MCP on loopback (Illustrator,
			     Photoshop) is a URL server running on your own computer, and calling that "Remote"
			     reads as a bug. -->
			<div class="flex flex-col gap-1.5">
				<span class="text-xs font-medium">Transport</span>
				<div class="flex gap-2">
					<Button
						variant={draft.transport === 'local' ? 'default' : 'outline'}
						size="sm"
						class="flex-1 gap-1.5"
						onclick={() => draft && (draft.transport = 'local')}
					>
						<ServerIcon class="size-3.5" />
						Command
					</Button>
					<Button
						variant={draft.transport === 'remote' ? 'default' : 'outline'}
						size="sm"
						class="flex-1 gap-1.5"
						onclick={() => draft && (draft.transport = 'remote')}
					>
						<GlobeIcon class="size-3.5" />
						URL
					</Button>
				</div>
			</div>

			{#if draft.transport === 'local'}
				<div class="flex flex-col gap-1.5">
					<label class="text-xs font-medium" for="mcp-command">
						Command <span class="text-muted-foreground font-normal">(required)</span>
					</label>
					<input
						id="mcp-command"
						class={FIELD}
						bind:value={draft.command}
						placeholder="npx"
						spellcheck="false"
					/>
					<p class="text-muted-foreground text-xs">
						Looked up on PATH. The <code>.cmd</code> shims npm and uv install are found automatically,
						so plain <code>npx</code> and <code>uvx</code> work.
					</p>
				</div>

				<div class="flex flex-col gap-1.5">
					<label class="text-xs font-medium" for="mcp-args">Arguments</label>
					<textarea
						id="mcp-args"
						class="{FIELD} min-h-20 resize-y font-mono"
						bind:value={draft.args}
						placeholder={'-y\n@modelcontextprotocol/server-filesystem\nC:/Users/you/Documents'}
						spellcheck="false"
					></textarea>
					<p class="text-muted-foreground text-xs">One per line, in order.</p>
				</div>

				<div class="flex flex-col gap-1.5">
					<label class="text-xs font-medium" for="mcp-cwd">Working directory <span class="text-muted-foreground font-normal">(optional)</span></label>
					<input id="mcp-cwd" class={FIELD} bind:value={draft.cwd} spellcheck="false" />
				</div>

				<div class="flex flex-col gap-1.5">
					<span class="text-xs font-medium"
						>Environment <span class="text-muted-foreground font-normal">(optional)</span></span
					>
					{#each draft.env as row, i (i)}
						<div class="flex gap-2">
							<input
								class="{FIELD} flex-1"
								bind:value={row.key}
								oninput={() => draft && tidyPairs(draft.env)}
								placeholder="GITHUB_PERSONAL_ACCESS_TOKEN"
								spellcheck="false"
							/>
							<input
								class="{FIELD} flex-1"
								bind:value={row.value}
								oninput={() => draft && tidyPairs(draft.env)}
								placeholder="${'{'}GITHUB_TOKEN{'}'}"
								spellcheck="false"
							/>
						</div>
					{/each}
					<p class="text-muted-foreground text-xs">
						Where a local server's credentials go. Write <code>{'${VAR}'}</code> to read the value from
						an environment variable instead of storing it in the file — Physalia saves the reference,
						not what it resolves to.
					</p>
				</div>
			{:else}
				<div class="flex flex-col gap-1.5">
					<label class="text-xs font-medium" for="mcp-url">
						URL <span class="text-muted-foreground font-normal">(required)</span>
					</label>
					<input
						id="mcp-url"
						class={FIELD}
						bind:value={draft.url}
						oninput={urlChanged}
						placeholder="https://mcp.example.com/mcp"
						spellcheck="false"
					/>
					<p class="text-muted-foreground text-xs">
						The web address the server answers on. Whoever runs the server gives you this — look
						for a "copy URL" button, or a setup page telling you to paste a URL into your MCP
						client.
					</p>
					<p class="text-muted-foreground text-xs">
						An address starting <code>http://localhost</code> or <code>http://127.0.0.1</code> is
						completely normal here, and it is not a mistake or a placeholder. It means the server
						is a program already running on this computer — Illustrator and Photoshop both offer
						their tools this way — and "localhost" is just how one program on your machine talks
						to another. Nothing leaves your computer. Anything else is an ordinary
						<code>https://</code> address out on the internet.
					</p>
				</div>

				<div class="flex flex-col gap-1.5">
					<span class="text-xs font-medium"
						>Headers <span class="text-muted-foreground font-normal">(optional)</span></span
					>
					{#each draft.headers as row, i (i)}
						<div class="flex gap-2">
							<input
								class="{FIELD} flex-1"
								bind:value={row.key}
								oninput={() => draft && tidyPairs(draft.headers)}
								placeholder="Authorization"
								spellcheck="false"
							/>
							<input
								class="{FIELD} flex-1"
								bind:value={row.value}
								oninput={() => draft && tidyPairs(draft.headers)}
								placeholder="Bearer ${'{'}MY_TOKEN{'}'}"
								spellcheck="false"
							/>
						</div>
					{/each}
					<p class="text-muted-foreground text-xs">
						Usually leave this empty: most hosted servers sign you in over OAuth, and the bridge
						opens a browser the first time one is reached. A header is for a server that wants a
						static token instead — write <code>{'${VAR}'}</code> to keep it out of the file.
					</p>
				</div>

				<div class="flex flex-col gap-1.5">
					<label class="text-xs font-medium" for="mcp-scope"
						>OAuth scopes <span class="text-muted-foreground font-normal">(optional)</span></label
					>
					<input
						id="mcp-scope"
						class={FIELD}
						bind:value={draft.scope}
						placeholder="read write"
						spellcheck="false"
					/>
					<p class="text-muted-foreground text-xs">
						Space-separated. Leave empty to let the server's own metadata decide — and ignored
						entirely by a server that takes a static token in a header instead.
					</p>
				</div>
			{/if}

			<div class="flex flex-wrap items-center justify-end gap-3">
				{#if missing.length > 0}
					<p class="text-muted-foreground mr-auto text-xs">
						Still needs {missing.join(' and ')}.
					</p>
				{/if}
				<Button variant="ghost" size="sm" onclick={cancel}>Cancel</Button>

				<!-- URL servers get the connect path as the PRIMARY action: it is a connection test in
				     every case, and for the OAuth-protected majority it also does the browser handshake
				     now rather than on the first solve of a node the user has not placed yet. Saving
				     without connecting stays available beside it, for a server that is not ready yet. -->
				{#if draft.transport === 'remote'}
					<Button variant="outline" size="sm" disabled={!canSave} onclick={() => save(false)}>
						{draft.original
							? 'Save'
							: hasOwnCredential
								? 'Add without connecting'
								: 'Add without signing in'}
					</Button>
					<Button size="sm" class="gap-1.5" disabled={!canSave || connecting !== null} onclick={() => save(true)}>
						{#if connecting !== null}
							<LoaderIcon class="size-3.5 animate-spin" />
							{hasOwnCredential ? 'Connecting…' : 'Waiting for sign-in…'}
						{:else if hasOwnCredential}
							<PlugIcon class="size-3.5" />
							Save & connect
						{:else}
							<LogInIcon class="size-3.5" />
							Save & sign in
						{/if}
					</Button>
				{:else}
					<Button size="sm" disabled={!canSave} onclick={() => save(false)}>
						{draft.original ? 'Save changes' : 'Add server'}
					</Button>
				{/if}
			</div>

			{#if draft.transport === 'remote'}
				<p class="text-muted-foreground text-xs">
					{#if hasOwnCredential}
						Connecting checks that the server answers and reports how many tools it offers. Your
						header is the credential, so no browser opens.
					{:else}
						Signing in opens your browser. Physalia remembers the result for this Windows account,
						so you should not be asked again — and the same button doubles as a connection test,
						since it reports how many tools the server offers.
					{/if}
				</p>
			{/if}
		</div>
	{:else}
		<!-- ------------------------------------------------------------------------- the list -->
		{#if servers.length > 0}
			<div class="mt-5 flex flex-col gap-2">
				{#each servers as server (server.name)}
					<div class="neu-raised flex items-start gap-3 rounded-md p-3">
						{#if server.transport === 'remote'}
							<GlobeIcon class="text-muted-foreground mt-0.5 size-4 shrink-0" />
						{:else}
							<ServerIcon class="text-muted-foreground mt-0.5 size-4 shrink-0" />
						{/if}

						<div class="min-w-0 flex-1">
							<p class="truncate text-sm font-medium">{server.name}</p>
							<p class="text-muted-foreground truncate font-mono text-xs">{summarize(server)}</p>
							{#if !server.runnable}
								<p class="mt-1 text-xs text-amber-700">
									Neither a command nor a URL — this entry cannot connect.
								</p>
							{/if}
						</div>

						{#if !readOnlyReason}
							<div class="flex shrink-0 gap-1">
								<Button
									variant="ghost"
									size="sm"
									class="size-8 p-0"
									disabled={!server.runnable || connecting !== null}
									title={server.transport === 'remote'
										? 'Sign in / test connection'
										: 'Test connection'}
									onclick={() => signIn(server.name)}
								>
									{#if connecting === server.name}
										<LoaderIcon class="size-3.5 animate-spin" />
									{:else}
										<LogInIcon class="size-3.5" />
									{/if}
								</Button>
								<Button
									variant="ghost"
									size="sm"
									class="size-8 p-0"
									title="Edit"
									onclick={() => startEdit(server)}
								>
									<PencilIcon class="size-3.5" />
								</Button>
								<Button
									variant="ghost"
									size="sm"
									class="size-8 p-0 {confirmingDelete === server.name ? 'text-red-600' : ''}"
									title={confirmingDelete === server.name ? 'Click again to remove' : 'Remove'}
									onclick={() => confirmDelete(server.name)}
								>
									<TrashIcon class="size-3.5" />
								</Button>
							</div>
						{/if}
					</div>
				{/each}
			</div>
		{:else}
			<div class="mt-6 flex flex-col items-center gap-4">
				<HappyFace />
				<p class="text-muted-foreground text-center text-sm">
					No MCP servers configured yet. Physalia ships none of its own — keeping a catalog of other
					people's servers is not this plug-in's job — so add whichever ones you use.
				</p>
			</div>
		{/if}

		{#if !readOnlyReason}
			<Button variant="outline" class="mt-4 h-auto w-full justify-start gap-2 py-2.5" onclick={startAdd}>
				<PlusIcon class="size-4 shrink-0" />
				Add a server
			</Button>
		{/if}
	{/if}
</div>
