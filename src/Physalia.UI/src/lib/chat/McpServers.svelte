<script lang="ts">
	// "Configure MCP connections" page. Lists the configured MCP servers and edits them one entry
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
	import TerminalIcon from '@lucide/svelte/icons/terminal';
	import SlidersIcon from '@lucide/svelte/icons/sliders-horizontal';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import LoaderIcon from '@lucide/svelte/icons/loader-circle';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import type { McpResult, McpServerPayload, UiMcpServer } from '$lib/bridge';

	interface Props {
		/** Servers read from the store, values verbatim (${VAR} intact). */
		servers: UiMcpServer[];
		/** Why the file cannot be written from here (JSON form, no wrapper), or null when it can. */
		/** Outcome of the last save/delete, or null. */
		result: McpResult | null;
		onsave: (entry: McpServerPayload) => void;
		ondelete: (name: string) => void;
		/** Connects to a saved server — for a remote one, this is what opens the browser sign-in. */
		onsignin: (name: string) => void;
		/**
		 * Connects to the entry being edited WITHOUT writing it, so a URL or a command can be
		 * checked before it is committed.
		 */
		ontest: (entry: McpServerPayload) => void;
		/** Parses a pasted setup command and connects to what it describes, writing nothing. */
		ontestcommand: (command: string) => void;
		/** Parses a pasted setup command, writes the entry it describes, then connects. */
		onsavecommand: (command: string) => void;
		onclose: () => void;
	}

	let {
		servers,
		result,
		onsave,
		ondelete,
		onsignin,
		ontest,
		ontestcommand,
		onsavecommand,
		onclose
	}: Props = $props();

	// Which server a connection attempt is running for, so its row can show a spinner. The host always
	// answers on setMcpResult (success or failure), and the effect below clears this when it does.
	let connecting = $state<string | null>(null);

	// Kept apart from `connecting` so the two buttons spin independently: a test writes nothing, and
	// showing the save button busy would suggest it had.
	let testing = $state(false);

	// Which screen the page is on. `draft` still owns the manual form — this covers the two screens
	// in front of it: the choice between the two ways in, and the paste box.
	let stage = $state<'list' | 'choose' | 'paste'>('list');

	// Which client's command is expected. It selects the EXAMPLE only: the parser detects the
	// grammar it is actually given, so pasting the other one is not a failure.
	let flavour = $state<'claude' | 'codex'>('claude');
	let command = $state('');

	$effect(() => {
		if (result) {
			connecting = null;
			testing = false;
		}
	});

	// A parsed command is committed and connected in one host call, so the commit button has its own
	// spinner rather than borrowing `connecting`, which is keyed by server name — a name this page
	// has not read yet.
	let committing = $state(false);

	$effect(() => {
		if (result) {
			committing = false;
		}
	});

	const EXAMPLES = {
		claude:
			'claude mcp add --transport http --header "Authorization: Bearer TOKEN" illustrator http://localhost:18412/v1/mcp',
		codex:
			'codex mcp add illustrator --url http://localhost:18412/v1/mcp --bearer-token-env-var TOKEN_VAR'
	};

	let hasCommand = $derived(command.trim().length > 0);

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
			// Matches the switch's leading option: the preselected mode and the leftmost button must
			// be the same one, or the form contradicts its own reading order.
			transport: 'remote',
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
		stage = 'list';
		confirmingDelete = null;
	}

	function startChoose() {
		stage = 'choose';
		confirmingDelete = null;
	}

	function startPaste() {
		stage = 'paste';
		command = '';
	}

	function testCommand() {
		if (!hasCommand || testing) {
			return;
		}

		testing = true;
		ontestcommand(command.trim());
	}

	function saveCommand() {
		if (!hasCommand || committing) {
			return;
		}

		committing = true;
		onsavecommand(command.trim());

		// Back to the list, the way the manual form does it by dropping its draft. Committing is
		// finished as far as this page is concerned: the entry is written and the outcome arrives in
		// the banner above the list, which is also where the new server appears. Staying on the paste
		// box would leave the command sitting there looking unsaved. `committing` is deliberately
		// left set — it is cleared when the result lands, so re-entering the page before then cannot
		// fire a second commit.
		stage = 'list';
	}

	function startEdit(server: UiMcpServer) {
		draft = draftOf(server);
		stage = 'list';
		confirmingDelete = null;
	}

	function cancel() {
		draft = null;
		stage = 'list';
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

	// The wire form of whatever is in the form right now. Shared by save and test so the two can
	// never disagree about what is being connected to — a test that checked a different shape from
	// the one about to be written would be worse than no test at all.
	function payload(d: Draft, signIn: boolean): McpServerPayload {
		return {
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
		};
	}

	function save(signIn = false) {
		if (!draft || !canSave) {
			return;
		}

		const d = draft;
		if (signIn) {
			connecting = d.name.trim();
		}

		onsave(payload(d, signIn));

		draft = null;
	}

	// Connect to the form's contents without saving. The draft deliberately STAYS open: the point of
	// a test is to fix what it reports, and closing the form would throw away the thing under test.
	function testConnection() {
		if (!draft || !canSave || testing) {
			return;
		}

		testing = true;
		ontest(payload(draft, false));
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
	<div class="mb-4 flex items-center justify-end">
		<Button variant="outline" size="sm" class="gap-1" onclick={onclose}>
			<ArrowLeftIcon class="size-4" />
			Go Back
		</Button>
	</div>

	<h2 class="text-lg font-semibold">MCP connections</h2>
	<p class="text-muted-foreground mt-1 text-sm">
		An MCP server lends its tools to the model. Add one here and it is written to
		Physalia; then place an <em>MCP Server</em> component in a harness and pick it by
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

			<!-- The transport switch. URL leads, because a URL is what a server hands you: a hosted
			     one gives you an https endpoint, and a desktop app exposing MCP on loopback
			     (Illustrator, Photoshop) gives you a localhost one — in both cases there is nothing to
			     install and nothing to look up. A Command entry is the one you have to assemble.
			     The two options are named after the field each one asks for — which is also the YAML
			     key it writes — because the real split is subprocess-vs-HTTP, NOT
			     this-machine-vs-elsewhere: a loopback server is a URL server running on your own
			     computer, and calling that "Remote" reads as a bug. URL is also the DEFAULT, because
			     the leading button and the preselected one have to agree — an npx entry is the
			     commoner one to add, but a switch whose highlight sits away from its first option
			     reads as a stuck control rather than as a considered default. -->
			<div class="flex flex-col gap-1.5">
				<span class="text-xs font-medium">Transport</span>
				<div class="flex gap-2">
					<Button
						variant={draft.transport === 'remote' ? 'default' : 'outline'}
						size="sm"
						class="flex-1 gap-1.5"
						onclick={() => draft && (draft.transport = 'remote')}
					>
						<GlobeIcon class="size-3.5" />
						URL
					</Button>
					<Button
						variant={draft.transport === 'local' ? 'default' : 'outline'}
						size="sm"
						class="flex-1 gap-1.5"
						onclick={() => draft && (draft.transport = 'local')}
					>
						<ServerIcon class="size-3.5" />
						Command
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

			<!-- Test on the left, then the commit pair on the right. Testing writes nothing, so it is
			     the one action here with no consequences and it sits away from the two that do.
			     There is no save-without-connecting button: "Save & connect" writes the entry FIRST
			     and reports the connection separately, so a server that is not running yet is still
			     saved — the button that used to exist for that case was doing nothing extra. -->
			<div class="flex flex-wrap items-center justify-end gap-3">
				<Button
					variant="outline"
					size="sm"
					class="mr-auto gap-1.5"
					disabled={!canSave || testing}
					onclick={testConnection}
				>
					{#if testing}
						<LoaderIcon class="size-3.5 animate-spin" />
						Testing…
					{:else}
						<PlugIcon class="size-3.5" />
						Test connection
					{/if}
				</Button>

				{#if missing.length > 0}
					<p class="text-muted-foreground text-xs">
						Still needs {missing.join(' and ')}.
					</p>
				{/if}

				<!-- URL servers commit through the connect path: it verifies the entry, and for the
				     OAuth-protected majority it also does the browser handshake now rather than on the
				     first solve of a node the user has not placed yet. -->
				{#if draft.transport === 'remote'}
					<Button
						size="sm"
						class="gap-1.5"
						disabled={!canSave || connecting !== null}
						onclick={() => save(true)}
					>
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

				<Button variant="ghost" size="sm" onclick={cancel}>Cancel</Button>
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
	{:else if stage === 'choose'}
		<!-- ------------------------------------------------------------------- how to connect -->
		<!-- Automatic leads because it is the shorter road AND the more reliable one: every host
		     publishing an MCP server already hands out a ready-made command, and pasting it cannot
		     swap a header's name for its value or mistake Claude Code's --scope for an OAuth scope.
		     Manual is the same form as always, for a server whose details arrived as prose. -->
		<div class="mt-5 flex flex-col gap-3">
			<button
				type="button"
				class="neu-raised flex items-center gap-3 rounded-md p-4 text-left transition-transform active:scale-[0.99]"
				onclick={startPaste}
			>
				<TerminalIcon class="text-muted-foreground size-5 shrink-0" />
				<span class="min-w-0 flex-1">
					<span class="block text-sm font-medium">Connect automatically</span>
					<span class="text-muted-foreground block text-xs">
						Paste the setup command the server gave you and Physalia reads the details out of it.
					</span>
				</span>
				<ChevronRightIcon class="text-muted-foreground size-4 shrink-0" />
			</button>

			<button
				type="button"
				class="neu-raised flex items-center gap-3 rounded-md p-4 text-left transition-transform active:scale-[0.99]"
				onclick={startAdd}
			>
				<SlidersIcon class="text-muted-foreground size-5 shrink-0" />
				<span class="min-w-0 flex-1">
					<span class="block text-sm font-medium">Connect manually</span>
					<span class="text-muted-foreground block text-xs">
						Fill in the URL or command, headers and scopes yourself.
					</span>
				</span>
				<ChevronRightIcon class="text-muted-foreground size-4 shrink-0" />
			</button>

			<div class="flex justify-end">
				<Button variant="ghost" size="sm" onclick={cancel}>Cancel</Button>
			</div>
		</div>
	{:else if stage === 'paste'}
		<!-- ---------------------------------------------------------------- paste a command -->
		<div class="neu-raised mt-5 flex flex-col gap-4 rounded-md p-4">
			<div class="flex flex-col gap-1.5">
				<span class="text-xs font-medium">Command for</span>
				<div class="flex gap-2">
					<Button
						variant={flavour === 'claude' ? 'default' : 'outline'}
						size="sm"
						class="flex-1"
						onclick={() => (flavour = 'claude')}
					>
						Claude Code
					</Button>
					<Button
						variant={flavour === 'codex' ? 'default' : 'outline'}
						size="sm"
						class="flex-1"
						onclick={() => (flavour = 'codex')}
					>
						Codex
					</Button>
				</div>
			</div>

			<div class="flex flex-col gap-1.5">
				<label class="text-xs font-medium" for="mcp-command-paste">Command</label>
				<textarea
					id="mcp-command-paste"
					class="{FIELD} min-h-24 resize-y font-mono text-xs"
					bind:value={command}
					spellcheck="false"
					placeholder={EXAMPLES[flavour]}
				></textarea>
				<p class="text-muted-foreground text-xs">
					Paste a command in the form of
					<code class="break-all">{EXAMPLES[flavour]}</code>
				</p>
			</div>

			<div class="flex flex-wrap items-center justify-end gap-3">
				<Button
					variant="outline"
					size="sm"
					class="mr-auto gap-1.5"
					disabled={!hasCommand || testing}
					onclick={testCommand}
				>
					{#if testing}
						<LoaderIcon class="size-3.5 animate-spin" />
						Testing…
					{:else}
						<PlugIcon class="size-3.5" />
						Test connection
					{/if}
				</Button>

				<Button
					size="sm"
					class="gap-1.5"
					disabled={!hasCommand || committing}
					onclick={saveCommand}
				>
					{#if committing}
						<LoaderIcon class="size-3.5 animate-spin" />
						Connecting…
					{:else}
						<PlugIcon class="size-3.5" />
						Save & connect
					{/if}
				</Button>

				<Button variant="ghost" size="sm" onclick={cancel}>Cancel</Button>
			</div>
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

		<Button variant="outline" class="mt-4 h-auto w-full justify-start gap-2 py-2.5" onclick={startChoose}>
			<PlusIcon class="size-4 shrink-0" />
			Add a server
		</Button>
	{/if}
</div>
