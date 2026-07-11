<script lang="ts">
	// Renders one assistant *turn group* — the run of assistant messages produced for a
	// single user prompt. Each agentic round is its own message (tool-result messages are
	// dropped by the host), so their content is flattened into one ordered item stream:
	// native <think> reasoning, tool calls, prose, then JSON, in the order they appeared.
	//
	// Everything before the first JSON deliverable is folded into a collapsible "Thinking"
	// section (open while streaming, auto-collapsing once the turn finishes); the JSON block
	// and anything after it stay visible as the answer. Native <think> reasoning inside that
	// section renders as plain muted text — never a nested collapsible. A turn that produces
	// no JSON renders inline, where reasoning gets its own "Thinking" collapsible (the only
	// one on screen in that case).
	import type { UiMessage, UiTool } from '$lib/bridge';
	import { splitThinking, splitContent } from '$lib/content';
	import Response from '$lib/components/ai-elements/response/response.svelte';
	import JsonBlock from './JsonBlock.svelte';
	import ChainOfThought from '$lib/components/ai-elements/chain-of-thought/chain-of-thought.svelte';
	import ChainOfThoughtHeader from '$lib/components/ai-elements/chain-of-thought/chain-of-thought-header.svelte';
	import ChainOfThoughtContent from '$lib/components/ai-elements/chain-of-thought/chain-of-thought-content.svelte';
	import Tool from '$lib/components/ai-elements/tool/tool.svelte';
	import ToolHeader from '$lib/components/ai-elements/tool/tool-header.svelte';
	import ToolContent from '$lib/components/ai-elements/tool/tool-content.svelte';
	import ToolInput from '$lib/components/ai-elements/tool/tool-input.svelte';
	import ToolOutput from '$lib/components/ai-elements/tool/tool-output.svelte';

	interface Props {
		messages: UiMessage[];
		/** True while the final message in the group is still streaming. */
		streaming?: boolean;
	}

	let { messages, streaming = false }: Props = $props();

	type Item =
		| { kind: 'reasoning'; text: string }
		| { kind: 'tool'; tool: UiTool }
		| { kind: 'text'; text: string }
		| { kind: 'json'; json: string };

	// Flatten every message into one ordered item stream, preserving the per-message order
	// the single-message renderer used: reasoning, then tools, then body segments.
	let items = $derived.by<Item[]>(() => {
		const out: Item[] = [];
		for (const message of messages) {
			const parts = splitThinking(message.text);
			if (parts.reasoning) {
				out.push({ kind: 'reasoning', text: parts.reasoning });
			}
			for (const tool of message.tools ?? []) {
				out.push({ kind: 'tool', tool });
			}
			for (const segment of splitContent(parts.body)) {
				out.push(
					segment.kind === 'json'
						? { kind: 'json', json: segment.json }
						: { kind: 'text', text: segment.text }
				);
			}
		}
		return out;
	});

	let firstJson = $derived(items.findIndex((item) => item.kind === 'json'));
	let hasJson = $derived(firstJson !== -1);
	let thinkingItems = $derived(hasJson ? items.slice(0, firstJson) : []);
	let answerItems = $derived(hasJson ? items.slice(firstJson) : items);

	// Open while streaming, auto-collapse once the turn finishes. Tracking the previous
	// streaming value (rather than forcing open = streaming every render) lets a manual
	// toggle stick until the streaming state actually changes.
	// svelte-ignore state_referenced_locally
	let open = $state(streaming);
	// svelte-ignore state_referenced_locally
	let wasStreaming = streaming;
	$effect(() => {
		if (wasStreaming !== streaming) {
			open = streaming;
			wasStreaming = streaming;
		}
	});
</script>

{#snippet renderItem(item: Item, insideThinking: boolean = false)}
	{#if item.kind === 'reasoning'}
		{#if insideThinking}
			<!-- Already inside the Thinking section — no nested collapsible. -->
			<Response content={item.text} class="text-muted-foreground text-sm" />
		{:else}
			<ChainOfThought>
				<ChainOfThoughtHeader>Thinking</ChainOfThoughtHeader>
				<ChainOfThoughtContent>
					<Response content={item.text} class="text-muted-foreground text-sm" />
				</ChainOfThoughtContent>
			</ChainOfThought>
		{/if}
	{:else if item.kind === 'tool'}
		<Tool>
			<ToolHeader type={item.tool.name} state={item.tool.state} />
			<ToolContent>
				{#if item.tool.input !== undefined}
					<ToolInput input={item.tool.input} />
				{/if}
				<ToolOutput output={item.tool.output} errorText={item.tool.errorText} />
			</ToolContent>
		</Tool>
	{:else if item.kind === 'json'}
		<JsonBlock json={item.json} />
	{:else}
		<Response content={item.text} />
	{/if}
{/snippet}

{#if hasJson && thinkingItems.length}
	<ChainOfThought {open}>
		<ChainOfThoughtHeader>Thinking</ChainOfThoughtHeader>
		<ChainOfThoughtContent>
			{#each thinkingItems as item, i (i)}
				{@render renderItem(item, true)}
			{/each}
		</ChainOfThoughtContent>
	</ChainOfThought>
	{#each answerItems as item, i (i)}
		{@render renderItem(item)}
	{/each}
{:else}
	{#each items as item, i (i)}
		{@render renderItem(item)}
	{/each}
{/if}
