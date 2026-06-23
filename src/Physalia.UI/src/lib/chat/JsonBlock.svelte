<script lang="ts">
	// A JSON payload rendered as a collapsed, copyable section so large graph dumps
	// (ghjson) don't flood the chat. Closed by default; the Code view (and its Shiki
	// highlighting) only mounts when expanded.
	import * as Collapsible from '$lib/components/ui/collapsible';
	import * as Code from '$lib/components/ai-elements/code';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import BracesIcon from '@lucide/svelte/icons/braces';

	interface Props {
		json: string;
	}

	let { json }: Props = $props();

	let open = $state(false);
	let lineCount = $derived(json.split('\n').length);
</script>

<Collapsible.Root bind:open class="neu-raised not-prose my-3 w-full rounded-md">
	<Collapsible.Trigger
		class="hover:bg-black/5 flex w-full items-center justify-between gap-2 rounded-md px-3 py-2 text-left text-sm"
	>
		<span class="text-muted-foreground flex items-center gap-2">
			<BracesIcon class="size-4 shrink-0" />
			<span class="font-medium">JSON</span>
			<span class="text-xs">· {lineCount} {lineCount === 1 ? 'line' : 'lines'}</span>
		</span>
		<ChevronDownIcon
			class="text-muted-foreground size-4 shrink-0 transition-transform {open ? 'rotate-180' : ''}"
		/>
	</Collapsible.Trigger>
	<Collapsible.Content
		class="data-[state=closed]:fade-out-0 data-[state=closed]:slide-out-to-top-2 data-[state=open]:slide-in-from-top-2 data-[state=closed]:animate-out data-[state=open]:animate-in outline-none"
	>
		<div class="max-h-96 overflow-auto rounded-b-md">
			<Code.Root code={json} lang="json" class="rounded-none border-0">
				<Code.CopyButton />
			</Code.Root>
		</div>
	</Collapsible.Content>
</Collapsible.Root>
