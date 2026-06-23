<script lang="ts">
	// "Add preset" page. Lists the bundled GhJSON preset definitions shipped under Files/PRESETS
	// (pushed by the host via setPresets) and places the chosen one onto the canvas through the
	// bridge. Each preset is a pre-wired Physalia pipeline; placing one drops it at the centre of
	// the current canvas view. The button shows the preset's file name; hovering reveals a popup
	// with the description taken from the file's metadata.description.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import type { UiPreset } from '$lib/bridge';

	interface Props {
		/** Presets available under Files/PRESETS. */
		presets: UiPreset[];
		/** Places the named preset file on the canvas (host action). */
		onplace: (file: string) => void;
		/** Returns to the chat view. */
		onclose: () => void;
	}

	let { presets, onplace, onclose }: Props = $props();

	// Button label = the file name without the redundant .ghjson extension.
	function presetName(file: string): string {
		return file.replace(/\.ghjson$/i, '');
	}
</script>

<div class="mx-auto flex w-full max-w-xl flex-col px-4 py-6">
	<div class="mb-4 flex items-center justify-between">
		<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={onclose}>
			<ArrowLeftIcon class="size-4" />
			Back to chat
		</Button>
	</div>

	<h2 class="text-lg font-semibold">Add a preset</h2>
	<p class="text-muted-foreground mt-1 text-sm">
		Drop a ready-made Physalia pipeline onto the canvas. Pick one below — it appears at the centre
		of your current view, ready to wire up. Hover a preset to see what it does.
	</p>

	{#if presets.length > 0}
		<div class="mt-4 flex flex-col gap-2">
			{#each presets as preset (preset.file)}
				<!-- group/relative so the description popup can be revealed on hover via group-hover. -->
				<div class="group relative">
					<Button
						variant="outline"
						class="h-auto w-full justify-start gap-2 py-2.5 text-left"
						onclick={() => onplace(preset.file)}
					>
						<PlusIcon class="size-4 shrink-0" />
						{presetName(preset.file)}
					</Button>

					<div
						class="neu-raised text-foreground pointer-events-none absolute top-full left-0 z-10 mt-1 hidden w-full rounded-md px-3 py-2 text-xs whitespace-pre-wrap group-hover:block"
					>
						{preset.description?.trim() ? preset.description : 'No description provided.'}
					</div>
				</div>
			{/each}
		</div>
	{:else}
		<div class="mt-6 flex flex-col items-center gap-4">
			<HappyFace />
			<p class="text-muted-foreground text-center text-sm">
				No presets found. Add <code>.ghjson</code> files to the plug-in's
				<code>Files/PRESETS</code> folder and they'll show up here.
			</p>
		</div>
	{/if}
</div>
