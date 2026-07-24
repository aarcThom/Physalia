<script lang="ts">
	import { onMount } from 'svelte';
	import Conversation from '$lib/components/ai-elements/conversation/conversation.svelte';
	import ConversationContent from '$lib/components/ai-elements/conversation/conversation-content.svelte';
	import ConversationScrollButton from '$lib/components/ai-elements/conversation/conversation-scroll-button.svelte';
	import Message from '$lib/components/ai-elements/message/core/message.svelte';
	import MessageContent from '$lib/components/ai-elements/message/core/message-content.svelte';
	import Response from '$lib/components/ai-elements/response/response.svelte';
	import Image from '$lib/components/ai-elements/image/image.svelte';
	import AssistantTurnGroup from '$lib/chat/AssistantTurnGroup.svelte';
	import Composer from '$lib/chat/Composer.svelte';
	import Setup from '$lib/chat/Setup.svelte';
	import Preset from '$lib/chat/Preset.svelte';
	import Grounding from '$lib/chat/Grounding.svelte';
	import ManualDefinition from '$lib/chat/ManualDefinition.svelte';
	import ConnectOptions from '$lib/chat/ConnectOptions.svelte';
	import {
		DropdownMenu,
		DropdownMenuTrigger,
		DropdownMenuContent,
		DropdownMenuItem,
		DropdownMenuSeparator
	} from '$lib/components/ui/dropdown-menu/index.js';
	import { Button } from '$lib/components/ui/button/index.js';
	import MenuIcon from '@lucide/svelte/icons/menu';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import { getProvider } from '$lib/chat/providers';
	import { cn } from '$lib/utils';
	import type {
		ClusterInfo,
		ClusterSelectionPayload,
		ComponentTabInfo,
		GroundingCategory,
		GroundingSelectionPayload,
		PythonFunctionInfo,
		ReferencedGeometryInfo,
		SetupResult,
		SnapshotMessagePayload,
		SubmitMessage,
		ToolsSelectionPayload,
		UiChat,
		UiMessage,
		UiPreset,
		UiState,
		UnitsOverridePayload
	} from '$lib/bridge';

	const BRIDGE_SCHEME = 'phbridge';

	let messages = $state<UiMessage[]>([]);
	let stream = $state<string | null>(null);
	let connected = $state(false);
	let busy = $state(false);
	let needsSetup = $state(false);
	let status = $state('');
	let configuredProviders = $state<string[]>([]);
	// Harness collapse state for the viewed Chat: whether its component group is hidden,
	// and how many components it holds (0 hides the show/hide-harness control).
	let collapsed = $state(false);
	let harnessCount = $state(0);

	// Grounding state for the grounding panel: whether a component catalog is wired (enables the
	// Composer's grounding button), the available tab → panels tree, and the current selection
	// (null = include everything).
	let groundingWired = $state(false);
	let groundingTree = $state<GroundingCategory[]>([]);
	let groundingSelection = $state<GroundingCategory[] | null>(null);
	// Whether the component grounding folds typed signatures into the prompt instead of bare names.
	let exposeSignatures = $state(false);
	// Grounded components grouped by tab, for the "/c/<tab>/<component>" staged autocomplete.
	let availableComponents = $state<ComponentTabInfo[]>([]);

	// Cluster grounding state: whether a cluster grounding is wired, the available clusters (from
	// Files/CLUSTERS), and the current selection (null = include everything).
	let clustersWired = $state(false);
	let availableClusters = $state<ClusterInfo[]>([]);
	let clusterSelection = $state<string[] | null>(null);

	// Tools grounding state: whether a tools grounding is wired, the tools on the canvas (for the Tools
	// page + "/t/" autocomplete), and the current selection (null = include everything).
	let toolsWired = $state(false);
	let availableTools = $state<string[]>([]);
	let toolsSelection = $state<string[] | null>(null);

	// Referenced Rhino Geometry and Python grounding state (read-only pages).
	let referencedGeometryWired = $state(false);
	let availableReferencedGeometry = $state<ReferencedGeometryInfo[]>([]);
	let pythonWired = $state(false);
	let pythonFunctions = $state<PythonFunctionInfo[]>([]);

	// Document-units grounding state: whether a units grounding is wired, the live document units, the
	// current override (null = use the document units), and the dropdown choices.
	let unitsWired = $state(false);
	let documentUnits = $state('');
	let unitsOverride = $state<string | null>(null);
	let unitOptions = $state<string[]>([]);

	// Human-tool state: whether a Geometry Snapshot tool is wired, whether generated geometry is
	// present right now (both together light the composer's geometry indicator), the tool's default
	// message, the current override (null = use the default), and whether an Add Image tool is
	// wired (without it image intake is fully disabled in the composer).
	let snapshotWired = $state(false);
	let snapshotGeometryPresent = $state(false);
	let snapshotDefaultMessage = $state('');
	let snapshotMessage = $state<string | null>(null);
	let imageToolWired = $state(false);

	// The grounding button opens the panel whenever any grounding kind — or human tool — is wired.
	let groundingAvailable = $derived(
		groundingWired ||
			clustersWired ||
			toolsWired ||
			referencedGeometryWired ||
			pythonWired ||
			unitsWired ||
			snapshotWired ||
			imageToolWired
	);

	// The cluster names currently exposed to the model (selection applied), for the "/c/" autocomplete.
	let includedClusterNames = $derived(
		availableClusters
			.map((c) => c.name)
			.filter((n) => clusterSelection === null || clusterSelection.includes(n))
	);

	// The tool names currently exposed to the model (selection applied), for the "/t/" autocomplete.
	let includedToolNames = $derived(
		availableTools.filter((n) => toolsSelection === null || toolsSelection.includes(n))
	);

	// Setup screen state. `needsSetup` (from the host) forces it when no provider is configured;
	// `manualSetup` lets the user open it from the header dropdown to add another provider later.
	let manualSetup = $state(false);
	let selectedProviderId = $state<string | null>(null);
	let setupResult = $state<SetupResult | null>(null);

	// Other full-screen pages opened from the header menu (mutually exclusive with the chat view
	// and with setup). null = none open.
	let panel = $state<'preset' | 'manualdef' | 'grounding' | null>(null);
	// Estimated token count from a Token Estimator wired downstream of the viewed ConversationLog,
	// pushed by the host; null = no estimator wired (or no count yet) → the counter hides.
	let tokenCount = $state<number | null>(null);

	// Bundled presets (from Files/PRESETS), pushed by the host.
	let presets = $state<UiPreset[]>([]);
	// Every Chat on the canvas (the bottom switcher row), pushed by the host.
	let chats = $state<UiChat[]>([]);

	let showSetup = $derived(needsSetup || manualSetup);
	// Only offer "Back to chat" when setup was opened manually (a provider already exists);
	// during first-run setup there is nothing to return to yet.
	let canClose = $derived(!needsSetup);
	// When a key-requiring provider's guide is open, the prompt box captures the key instead.
	let keyProvider = $derived.by(() => {
		if (!showSetup) {
			return null;
		}
		const provider = getProvider(selectedProviderId);
		return provider && provider.needsKey ? { id: provider.id, label: provider.label } : null;
	});

	onMount(() => {
		// C# -> JS surface. The host calls these via ExecuteScript on its UI timer.
		window.physalia = {
			setHistory: (next) => {
				messages = next ?? [];
			},
			setStream: (text) => {
				stream = text;
			},
			setState: (next: UiState) => {
				connected = next.connected;
				busy = next.busy;
				needsSetup = next.needsSetup ?? false;
				status = next.status ?? '';
				configuredProviders = next.configuredProviders ?? [];
				collapsed = next.collapsed ?? false;
				harnessCount = next.harnessCount ?? 0;
				groundingWired = next.groundingWired ?? false;
				groundingTree = next.groundingTree ?? [];
				groundingSelection = next.groundingSelection ?? null;
				exposeSignatures = next.exposeSignatures ?? false;
				availableComponents = next.availableComponents ?? [];
				clustersWired = next.clustersWired ?? false;
				availableClusters = next.availableClusters ?? [];
				clusterSelection = next.clusterSelection ?? null;
				toolsWired = next.toolsWired ?? false;
				availableTools = next.availableTools ?? [];
				toolsSelection = next.toolsSelection ?? null;
				referencedGeometryWired = next.referencedGeometryWired ?? false;
				availableReferencedGeometry = next.availableReferencedGeometry ?? [];
				pythonWired = next.pythonWired ?? false;
				pythonFunctions = next.pythonFunctions ?? [];
				unitsWired = next.unitsWired ?? false;
				documentUnits = next.documentUnits ?? '';
				unitsOverride = next.unitsOverride ?? null;
				unitOptions = next.unitOptions ?? [];
				snapshotWired = next.snapshotWired ?? false;
				snapshotGeometryPresent = next.snapshotGeometryPresent ?? false;
				snapshotDefaultMessage = next.snapshotDefaultMessage ?? '';
				snapshotMessage = next.snapshotMessage ?? null;
				imageToolWired = next.imageToolWired ?? false;
			},
			setSetupResult: (result) => {
				setupResult = result;
			},
			setTokenCount: (count) => {
				tokenCount = count;
			},
			setPresets: (next) => {
				presets = next ?? [];
			},
			setChats: (next) => {
				chats = next ?? [];
			}
		};

		// C# pulls the pending outgoing message (text + images) from here after it
		// intercepts the phbridge://submit navigation.
		window.__physaliaTake = () => {
			const pending = window.__physaliaPending ?? '';
			window.__physaliaPending = '';
			return pending;
		};

		return () => {
			delete window.physalia;
			delete window.__physaliaTake;
		};
	});

	// JS -> C#: poke the host with a custom-URI navigation it cancels.
	//  - Text-only: carried directly in the URL query (small, and the proven path).
	//  - With images: the full payload is stashed on window (base64 blows past URL
	//    limits) and the host pulls it back with __physaliaTake().
	function send(message: SubmitMessage) {
		if (message.images.length === 0) {
			window.location.href = `${BRIDGE_SCHEME}://submit?text=${encodeURIComponent(message.text)}`;
			return;
		}

		const json = JSON.stringify(message);

		// Primary (WebView2): hand the payload to the host via the message channel. Reliable for
		// large payloads, unlike pulling the JSON back after a cancelled navigation (that runs
		// in a transient context and returns null).
		const webview = (window as unknown as { chrome?: { webview?: { postMessage?: (m: string) => void } } })
			.chrome?.webview;
		if (webview?.postMessage) {
			webview.postMessage(json);
			return;
		}

		// Fallback (non-WebView2, e.g. Mac WKWebView): stash + navigate; host pulls it back.
		window.__physaliaPending = json;
		window.location.href = `${BRIDGE_SCHEME}://submit?images=1`;
	}

	// Ask the host to cancel the active inference on the wired pipeline's LLM Call. The composer's
	// cancel button is enabled only while `busy`, so this fires only when a request is in flight.
	function cancel() {
		window.location.href = `${BRIDGE_SCHEME}://cancel`;
	}

	// Open an external setup link in the system browser (the host cancels the nav and shells out).
	function openLink(url: string) {
		window.location.href = `${BRIDGE_SCHEME}://open?url=${encodeURIComponent(url)}`;
	}

	// Ask the host to place a ConversationLog on the canvas and wire it to this Chat. The next state
	// tick reports `connected`, which dismisses the connect screen on its own.
	function connectConversationLog() {
		window.location.href = `${BRIDGE_SCHEME}://connectconversationlog`;
	}

	// Ask the host to place the chosen bundled preset (.ghjson) on the canvas, then return to chat.
	function placePreset(file: string) {
		window.location.href = `${BRIDGE_SCHEME}://placepreset?file=${encodeURIComponent(file)}`;
		panel = null;
	}

	// Ask the host to clear signals / conversations / histories from every Physalia component in
	// the open document.
	function clearAllComponents() {
		window.location.href = `${BRIDGE_SCHEME}://clearall`;
	}

	// Ask the host to collapse/expand the viewed Chat's harness group on the canvas. The next
	// state tick reports the new `collapsed` flag, which updates the menu label.
	function toggleHarness() {
		window.location.href = `${BRIDGE_SCHEME}://togglecollapse`;
	}

	// Switch the window to view another Chat component (its conversation log history, or the default
	// screen when it has none). The next state tick re-pushes that component's history/state.
	function selectChat(id: string) {
		window.location.href = `${BRIDGE_SCHEME}://selectchat?id=${encodeURIComponent(id)}`;
	}

	// Apply a grounding selection to the wired ConversationLog. The payload {all, leaves} is small enough to
	// carry in the URL query (unlike image payloads). all=true returns to include-everything.
	function setGrounding(payload: GroundingSelectionPayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setgrounding?sel=${encodeURIComponent(json)}`;
	}

	// Toggle typed component signatures in the grounded system prompt (instead of bare names).
	function setSignatures(on: boolean) {
		window.location.href = `${BRIDGE_SCHEME}://setsignatures?on=${on ? '1' : '0'}`;
	}

	// Apply a cluster selection to the wired ConversationLog. all=true returns to include-everything.
	function setClusters(payload: ClusterSelectionPayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setclusters?sel=${encodeURIComponent(json)}`;
	}

	// Apply a tools selection to the wired ConversationLog. all=true returns to include-every-present-tool.
	function setTools(payload: ToolsSelectionPayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://settools?sel=${encodeURIComponent(json)}`;
	}

	// Apply a document-units override to the wired ConversationLog. reset=true returns to the live doc units.
	function setUnits(payload: UnitsOverridePayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setunits?sel=${encodeURIComponent(json)}`;
	}

	// Apply a geometry-snapshot message override to the wired ConversationLog. reset=true returns to
	// the grounding's default message.
	function setSnapshotMessage(payload: SnapshotMessagePayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setsnapshotmessage?sel=${encodeURIComponent(json)}`;
	}

	// Ask the host to capture a viewport snapshot of the generated geometry and send it (with its
	// predefined message) as its own user message. Fired by the composer's geometry button.
	function sendSnapshot() {
		window.location.href = `${BRIDGE_SCHEME}://sendsnapshot`;
	}

	// Hand a pasted API key to the host, which writes it to API_KEY_CONFIG.YAML and reports back
	// via setSetupResult. encodeURIComponent keeps the key intact in the URL (no literal '+').
	function saveKey(providerId: string, key: string) {
		window.location.href =
			`${BRIDGE_SCHEME}://savekey?provider=${encodeURIComponent(providerId)}&key=${encodeURIComponent(key)}`;
	}

	function selectProvider(id: string | null) {
		selectedProviderId = id;
		setupResult = null; // a fresh page shouldn't carry the previous provider's result
	}

	function openSetup() {
		manualSetup = true;
		panel = null;
		selectedProviderId = null;
		setupResult = null;
	}

	function closeSetup() {
		manualSetup = false;
		selectedProviderId = null;
		setupResult = null;
	}

	// Open one of the pages (preset / manual definition / grounding), leaving setup.
	function openPanel(which: 'preset' | 'manualdef' | 'grounding') {
		panel = which;
		manualSetup = false;
		selectedProviderId = null;
		setupResult = null;
	}

	function closePanel() {
		panel = null;
	}

	let isEmpty = $derived(messages.length === 0 && !stream);
	// Provider configured (not setup) but no ConversationLog wired and nothing said yet: offer the
	// connect-a-conversation log / workflow / configure options instead of a bare empty conversation.
	let showConnect = $derived(!showSetup && !connected && isEmpty);

	// Group the flat message list into render units: each user message stands alone, while
	// a run of consecutive assistant messages (the agentic rounds for one prompt) collapses
	// into a single assistant turn. The live `stream` partial is appended to the active
	// assistant turn (or starts one) so the in-progress round renders inside the same group.
	type RenderGroup =
		| { kind: 'user'; key: string; message: UiMessage }
		| { kind: 'assistant'; key: string; messages: UiMessage[]; streaming: boolean };

	let groups = $derived.by<RenderGroup[]>(() => {
		const out: RenderGroup[] = [];
		let current: Extract<RenderGroup, { kind: 'assistant' }> | null = null;

		for (const message of messages) {
			if (message.role === 'assistant') {
				if (!current) {
					current = { kind: 'assistant', key: message.id, messages: [], streaming: false };
					out.push(current);
				}
				current.messages.push(message);
			} else {
				current = null;
				out.push({ kind: 'user', key: message.id, message });
			}
		}

		if (stream) {
			if (!current) {
				current = { kind: 'assistant', key: 'live', messages: [], streaming: false };
				out.push(current);
			}
			current.messages.push({ id: 'live', role: 'assistant', text: stream, tools: [] });
			current.streaming = true;
		}

		return out;
	});
</script>

<main class="bg-transparent text-foreground relative flex h-screen flex-col overflow-hidden">
	<!-- Header: a small menu at the very top. Its dropdown reopens the provider setup screen,
	     opens the preset gallery, or the manual-definition page. The clear-all button on the
	     right wipes every Physalia component's signals / conversations in the open document. -->
	<!-- pr matches the chat content's right edge: p-4 gutter (16px) + the reserved scrollbar gutter
	     (14px, see app.css ::-webkit-scrollbar + scrollbar-gutter), so "Clear all components"
	     right-aligns with the user message bubbles. -->
	<header class="flex shrink-0 items-center gap-2 py-1 pl-2 pr-[30px]">
		<DropdownMenu>
			<DropdownMenuTrigger>
				{#snippet child({ props })}
					<Button variant="ghost" size="sm" class="gap-1 px-2" title="Menu" {...props}>
						<MenuIcon class="size-4" />
						<ChevronDownIcon class="size-3.5" />
					</Button>
				{/snippet}
			</DropdownMenuTrigger>
			<DropdownMenuContent align="start" class="w-60">
				<DropdownMenuItem class="whitespace-nowrap" onSelect={openSetup}>
					Set up providers…
				</DropdownMenuItem>
				<DropdownMenuSeparator />
				<DropdownMenuItem class="whitespace-nowrap" onSelect={() => openPanel('preset')}>
					Add preset
				</DropdownMenuItem>
				<DropdownMenuSeparator />
				<DropdownMenuItem class="whitespace-nowrap" onSelect={() => openPanel('manualdef')}>
					Add new manual definition
				</DropdownMenuItem>
				{#if harnessCount > 0}
					<DropdownMenuSeparator />
					<DropdownMenuItem class="whitespace-nowrap" onSelect={toggleHarness}>
						{collapsed ? `Show harness (${harnessCount})` : `Hide harness (${harnessCount})`}
					</DropdownMenuItem>
				{/if}
			</DropdownMenuContent>
		</DropdownMenu>
		<span class="text-muted-foreground text-xs font-medium">Physalia Chat</span>
		<Button
			variant="outline"
			size="sm"
			class="ml-auto gap-1.5"
			title="Clear signals, conversations and histories from every Physalia component in the open document"
			onclick={clearAllComponents}
		>
			<Trash2Icon class="size-3.5" />
			Clear all components
		</Button>
	</header>

	<!-- flex-1 + min-h-0 lets this region size to the space left by the composer and
	     shrink, so the composer stays pinned at the bottom and the chat scrolls within. -->
	<div class="relative min-h-0 flex-1">
		<Conversation class="h-full">
			<ConversationContent class="min-h-0 flex-1 overflow-y-auto [scrollbar-gutter:stable]">
			{#if showSetup}
				<Setup
					selectedId={selectedProviderId}
					{setupResult}
					{canClose}
					{configuredProviders}
					onselect={selectProvider}
					onopenlink={openLink}
					onclose={closeSetup}
				/>
			{:else if panel === 'preset'}
				<Preset {presets} onplace={placePreset} onclose={closePanel} />
			{:else if panel === 'grounding'}
				<Grounding
					tree={groundingTree}
					selection={groundingSelection}
					{exposeSignatures}
					clusters={availableClusters}
					clusterSelection={clusterSelection}
					tools={availableTools}
					{toolsSelection}
					referencedGeometry={availableReferencedGeometry}
					{pythonFunctions}
					{unitsWired}
					{documentUnits}
					{unitsOverride}
					{unitOptions}
					{snapshotWired}
					{snapshotDefaultMessage}
					{snapshotMessage}
					{imageToolWired}
					onapply={setGrounding}
					onapplysignatures={setSignatures}
					onapplyclusters={setClusters}
					onapplytools={setTools}
					onapplyunits={setUnits}
					onapplysnapshot={setSnapshotMessage}
					onclose={closePanel}
				/>
			{:else if panel === 'manualdef'}
				<ManualDefinition onclose={closePanel} />
			{:else if showConnect}
				<ConnectOptions
						onconnectconversationlog={connectConversationLog}
						onpreset={() => openPanel('preset')}
						onconfigure={openSetup}
					/>
			{:else}
			{#if isEmpty}
				<div class="text-muted-foreground flex h-full flex-col items-center justify-center gap-1 text-center">
					<p class="text-sm font-medium">Physalia chat</p>
					<p class="text-xs">Send a message to start the conversation.</p>
				</div>
			{/if}

			{#each groups as group (group.key)}
				{#if group.kind === 'user'}
					<Message from="user" feedback={group.message.feedback}>
						{#if group.message.images?.length}
							<div class="flex flex-wrap justify-end gap-2">
								{#each group.message.images as image, i (i)}
									<Image
										base64={image.base64}
										mediaType={image.mediaType}
										alt="attachment"
										class="neu-raised-sm max-h-48 w-auto rounded-lg"
									/>
								{/each}
							</div>
						{/if}
						{#if group.message.text}
							<MessageContent>
								<div class="whitespace-pre-wrap">{group.message.text}</div>
							</MessageContent>
						{/if}
					</Message>
				{:else}
					<Message from="assistant">
						<MessageContent>
							<AssistantTurnGroup messages={group.messages} streaming={group.streaming} />
						</MessageContent>
					</Message>
				{/if}
			{/each}
			{/if}
		</ConversationContent>
			<ConversationScrollButton />
		</Conversation>
	</div>

	{#if status && !showSetup}
		<div class="text-muted-foreground shrink-0 px-4 py-1 text-xs">{status}</div>
	{/if}

	<div class="shrink-0 p-3">
		<Composer
			disconnected={!connected}
			{busy}
			disabled={showSetup}
			apiKeyProvider={keyProvider}
			groundingWired={groundingAvailable}
			snapshotArmed={snapshotWired && snapshotGeometryPresent}
			{imageToolWired}
			clusterNames={includedClusterNames}
			toolNames={includedToolNames}
			componentTabs={availableComponents}
			onsend={send}
			onsnapshot={sendSnapshot}
			onsavekey={saveKey}
			ongrounding={() => openPanel('grounding')}
			oncancel={cancel}
		/>
	</div>

	<!-- Switcher row at the very bottom: one emoji per Chat on the canvas — its assigned
	     sea/ocean glyph, matching the component's canvas icon so the two are easy to pair. The
	     active chat sits on a raised accent ring; a chat with no recorded history is dimmed.
	     Clicking an emoji views that Chat's conversation log history (or the default screen when it
	     has none). New emojis appear as Chats are placed. -->
	{#if chats.length > 0}
		<div class="flex shrink-0 items-center justify-center gap-1 pb-2">
			{#each chats as box (box.id)}
				<button
					type="button"
					onclick={() => selectChat(box.id)}
					aria-pressed={box.active}
					title={box.active
						? 'Current chat'
						: box.hasHistory
							? 'Switch to this chat (has history)'
							: 'Switch to this chat'}
					class="group flex items-center justify-center rounded-full p-0.5"
				>
					<span
						class={cn(
							'flex size-6 items-center justify-center rounded-full text-sm leading-none transition',
							box.active
								? 'bg-[var(--neu-accent)]/15 shadow-[var(--neu-shadow-sm)]'
								: box.hasHistory
									? 'opacity-100 group-hover:bg-muted-foreground/10'
									: 'opacity-40 group-hover:opacity-70'
						)}
					>{box.emoji}</span>
				</button>
			{/each}
		</div>
	{/if}

	<!-- Token counter, pinned to the window's bottom-right corner. Shown only when a Token
	     Estimator is wired downstream of this chat's ConversationLog — the host pushes null otherwise
	     and the counter disappears. -->
	{#if tokenCount !== null && !showSetup}
		<div
			class="text-muted-foreground absolute right-3 bottom-2 text-[11px] tabular-nums select-none"
			title="Estimated tokens (Token Estimator on this chat's pipeline)"
		>
			{tokenCount.toLocaleString()} tokens
		</div>
	{/if}
</main>
