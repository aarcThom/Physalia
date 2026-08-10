<script lang="ts">
	// "Add preset" page. Lists the preset harnesses under Files/PRESETS (pushed by the host via
	// setPresets) and loads the chosen one through the bridge. A preset is an ordinary Grasshopper file
	// holding one harness's worth of pipeline, so it becomes a new harness's contents wholesale.
	//
	// The library is divided by where a preset came from — Physalia (shipped), User (saved from a
	// harness), Community (not populated yet) — and the host pushes them already grouped and sorted, so
	// the headings below are just a run-length pass over that order. Empty folders never appear.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import type { UiPreset } from '$lib/bridge';

	interface Props {
		/** Presets available under Files/PRESETS, in the host's grouped order. */
		presets: UiPreset[];
		/** Places the given preset (by its library-relative path) on the canvas (host action). */
		onplace: (file: string) => void;
		/** Returns to the chat view. */
		onclose: () => void;
	}

	let { presets, onplace, onclose }: Props = $props();

	// What each folder is called on the page, and the blurb under its heading.
	const FOLDER_LABELS: Record<string, string> = {
		Physalia: 'Physalia',
		User: 'Yours',
		Community: 'Community'
	};

	// The host's order is already folder-by-folder, so grouping is a single pass — no sorting here, or
	// the page and the library would disagree about precedence.
	let groups = $derived.by(() => {
		const out: { folder: string; label: string; items: UiPreset[] }[] = [];
		for (const preset of presets) {
			const last = out[out.length - 1];
			if (last && last.folder === preset.folder) {
				last.items.push(preset);
			} else {
				out.push({
					folder: preset.folder,
					label: FOLDER_LABELS[preset.folder] ?? preset.folder,
					items: [preset]
				});
			}
		}
		return out;
	});
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
		Load a ready-made Physalia pipeline. Pick one below and it arrives as a new harness on your
		canvas, ready to use — whatever is already there keeps running.
	</p>

	{#if groups.length > 0}
		{#each groups as group (group.folder)}
			<h3 class="text-muted-foreground mt-5 text-xs font-semibold tracking-wide uppercase">
				{group.label}
			</h3>

			<div class="mt-2 flex flex-col gap-2">
				{#each group.items as preset (preset.file)}
					<!-- group/relative so the description popup can be revealed on hover via group-hover. -->
					<div class="group relative">
						<Button
							variant="outline"
							class="h-auto w-full justify-start gap-2 py-2.5 text-left"
							onclick={() => onplace(preset.file)}
						>
							<PlusIcon class="size-4 shrink-0" />
							{preset.name}
						</Button>

						<div
							class="neu-raised text-foreground pointer-events-none absolute top-full left-0 z-10 mt-1 hidden w-full rounded-md px-3 py-2 text-xs whitespace-pre-wrap group-hover:block"
						>
							{preset.description?.trim()
							? preset.description
							: 'No description. Add a Harness Notes panel inside the harness and save it again.'}
						</div>
					</div>
				{/each}
			</div>
		{/each}
	{:else}
		<div class="mt-6 flex flex-col items-center gap-4">
			<HappyFace />
			<p class="text-muted-foreground text-center text-sm">
				No presets found. Save one with <em>Save Harness as Preset</em> — on a Harness component's
				right-click menu, or on the <em>Harness</em> widget shown while you are inside one — and it
				will appear here under <em>Yours</em>.
			</p>
		</div>
	{/if}
</div>
