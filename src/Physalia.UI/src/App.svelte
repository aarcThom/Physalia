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
	import type { SubmitMessage, UiMessage, UiState } from '$lib/bridge';

	const BRIDGE_SCHEME = 'phbridge';

	let messages = $state<UiMessage[]>([]);
	let stream = $state<string | null>(null);
	let connected = $state(false);
	let busy = $state(false);
	let needsSetup = $state(false);
	let status = $state('');

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

	let isEmpty = $derived(messages.length === 0 && !stream);

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
	<!-- flex-1 + min-h-0 lets this region size to the space left by the composer and
	     shrink, so the composer stays pinned at the bottom and the chat scrolls within. -->
	<div class="relative min-h-0 flex-1">
		<Conversation class="h-full">
			<ConversationContent class="min-h-0 flex-1 overflow-y-auto">
			{#if needsSetup}
				<Setup />
			{:else}
			{#if isEmpty}
				<div class="text-muted-foreground flex h-full flex-col items-center justify-center gap-1 text-center">
					<p class="text-sm font-medium">Physalia chat</p>
					<p class="text-xs">
						{connected ? 'Send a message to start the conversation.' : 'Connect this Chatbox to a Recorder to begin.'}
					</p>
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

	{#if status}
		<div class="text-muted-foreground shrink-0 border-t px-4 py-1 text-xs">{status}</div>
	{/if}

	<div class="shrink-0 border-t p-3">
		<Composer disconnected={!connected} {busy} disabled={needsSetup} onsend={send} />
	</div>
</main>
