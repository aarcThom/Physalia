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
	import type { SetupResult, SubmitMessage, UiMessage, UiState } from '$lib/bridge';

	const BRIDGE_SCHEME = 'phbridge';

	let messages = $state<UiMessage[]>([]);
	let stream = $state<string | null>(null);
	let connected = $state(false);
	let busy = $state(false);
	let needsSetup = $state(false);
	let status = $state('');
	let configuredProviders = $state<string[]>([]);

	// Setup screen state. `needsSetup` (from the host) forces it when no provider is configured;
	// `manualSetup` lets the user open it from the header dropdown to add another provider later.
	let manualSetup = $state(false);
	let selectedProviderId = $state<string | null>(null);
	let setupResult = $state<SetupResult | null>(null);

	// Other full-screen pages opened from the header menu (mutually exclusive with the chat view
	// and with setup). null = none open.
	let panel = $state<'preset' | 'manualdef' | null>(null);
	// Bundled preset .ghjson file names (from Files/PRESETS), pushed by the host.
	let presets = $state<string[]>([]);

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
			},
			setSetupResult: (result) => {
				setupResult = result;
			},
			setPresets: (files) => {
				presets = files ?? [];
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

	// Open an external setup link in the system browser (the host cancels the nav and shells out).
	function openLink(url: string) {
		window.location.href = `${BRIDGE_SCHEME}://open?url=${encodeURIComponent(url)}`;
	}

	// Ask the host to place a Recorder on the canvas and wire it to this Chatbox. The next state
	// tick reports `connected`, which dismisses the connect screen on its own.
	function connectRecorder() {
		window.location.href = `${BRIDGE_SCHEME}://connectrecorder`;
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

	// Open one of the header-menu pages (preset / manual definition), leaving setup.
	function openPanel(which: 'preset' | 'manualdef') {
		panel = which;
		manualSetup = false;
		selectedProviderId = null;
		setupResult = null;
	}

	function closePanel() {
		panel = null;
	}

	let isEmpty = $derived(messages.length === 0 && !stream);
	// Provider configured (not setup) but no Recorder wired and nothing said yet: offer the
	// connect-a-recorder / workflow / configure options instead of a bare empty conversation.
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

<main class="bg-background text-foreground flex h-screen flex-col overflow-hidden">
	<!-- Header: a small menu at the very top. Its dropdown reopens the provider setup screen,
	     opens the preset gallery, or the manual-definition page. The clear-all button on the
	     right wipes every Physalia component's signals / conversations in the open document. -->
	<header class="flex shrink-0 items-center gap-2 border-b px-2 py-1">
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
			<ConversationContent class="min-h-0 flex-1 overflow-y-auto">
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
			{:else if panel === 'manualdef'}
				<ManualDefinition onclose={closePanel} />
			{:else if showConnect}
				<ConnectOptions onconnectrecorder={connectRecorder} onconfigure={openSetup} />
			{:else}
			{#if isEmpty}
				<div class="text-muted-foreground flex h-full flex-col items-center justify-center gap-1 text-center">
					<p class="text-sm font-medium">Physalia chat</p>
					<p class="text-xs">Send a message to start the conversation.</p>
				</div>
			{/if}

			{#each groups as group (group.key)}
				{#if group.kind === 'user'}
					<Message from="user">
						{#if group.message.images?.length}
							<div class="flex flex-wrap justify-end gap-2">
								{#each group.message.images as image, i (i)}
									<Image
										base64={image.base64}
										mediaType={image.mediaType}
										alt="attachment"
										class="max-h-48 w-auto rounded-lg border"
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
		<div class="text-muted-foreground shrink-0 border-t px-4 py-1 text-xs">{status}</div>
	{/if}

	<div class="shrink-0 border-t p-3">
		<Composer
			disconnected={!connected}
			{busy}
			disabled={showSetup}
			apiKeyProvider={keyProvider}
			onsend={send}
			onsavekey={saveKey}
		/>
	</div>
</main>
