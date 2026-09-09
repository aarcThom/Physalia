<script lang="ts">
	// "Add preset" page. Lists the preset harnesses under Files/PRESETS (pushed by the host via
	// setPresets) and loads the chosen one through the bridge. A preset is an ordinary Grasshopper file
	// holding one harness's worth of pipeline, so it becomes a new harness's contents wholesale.
	//
	// The library is divided by where a preset came from — Physalia (shipped), User (saved from a
	// harness), Community (not populated yet) — and the host pushes them already grouped and sorted, so
	// the headings below are just a run-length pass over that order. Empty folders never appear.
	//
	// The fourth folder, AI, is the exception: those harnesses were written by a model rather than by
	// hand, so they are folded away behind one pink Experimental button, with a warning above them,
	// instead of sitting in the gallery as though they had been finished. Same rows, same
	// descriptions, same load path — the only difference is that you have to ask for them.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import FlaskConicalIcon from '@lucide/svelte/icons/flask-conical';
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

	/** Gap between a preset row and its description, and the closest the description gets to the window edge. */
	const GAP = 4;
	const EDGE = 8;

	/** The description being shown, already resolved to VIEWPORT coordinates. */
	interface Tip {
		text: string;
		left: number;
		width: number;
		/** Exactly one of top/bottom is set — bottom anchors it above the row. */
		top: number | null;
		bottom: number | null;
		maxHeight: number;
	}

	// The hovered preset's description is drawn as ONE fixed-position panel, not as an absolutely
	// positioned popup inside each row. That is the whole point: this page lives in the window's own
	// scroller, and an absolutely positioned panel counts towards that scroller's scrollable overflow —
	// so revealing the description under the LAST preset grew the scroll extent, which fought the
	// scrollbar and flickered. A fixed element belongs to no ancestor's scroll region, so nothing
	// reflows when it appears, and nothing clips it either.
	let tip = $state<Tip | null>(null);
	let anchor: HTMLElement | null = null;

	function descriptionOf(preset: UiPreset): string {
		return preset.description?.trim()
			? preset.description
			: 'No description. Open the harness panel, describe what this pipeline is for, and save it again.';
	}

	// Opens on whichever side of the row has more room and is capped to it, so the description is
	// always whole and always on screen — the bottom of the list included.
	function place(text: string) {
		if (!anchor) {
			return;
		}
		const rect = anchor.getBoundingClientRect();
		const below = window.innerHeight - rect.bottom - GAP - EDGE;
		const above = rect.top - GAP - EDGE;
		const openBelow = below >= above;
		tip = {
			text,
			left: rect.left,
			width: rect.width,
			top: openBelow ? rect.bottom + GAP : null,
			bottom: openBelow ? null : window.innerHeight - rect.top + GAP,
			maxHeight: Math.max(openBelow ? below : above, 0)
		};
	}

	function show(event: Event, preset: UiPreset) {
		anchor = event.currentTarget as HTMLElement;
		place(descriptionOf(preset));
	}

	function hide() {
		anchor = null;
		tip = null;
	}

	// Scrolling or resizing moves the row the description is pinned to, and a fixed panel does not
	// travel with it — so follow the row rather than leaving its text stranded. Capture phase: the
	// scroll happens on the page's own scroller and scroll events do not bubble.
	function follow() {
		if (anchor && tip) {
			place(tip.text);
		}
	}

	// What each folder is called on the page, and the blurb under its heading.
	const FOLDER_LABELS: Record<string, string> = {
		Physalia: 'Physalia',
		User: 'Yours',
		Community: 'Community'
	};

	/** The library folder holding the model-written harnesses. Matches PresetLibrary.ExperimentalFolder. */
	const EXPERIMENTAL_FOLDER = 'AI';

	// In the author's own voice, and unhedged: the point is that these are a stopgap and the user
	// should know it before opening one. The emoji is written as an escape rather than pasted, so the
	// text survives however this bundle is embedded and extracted on the way to the WebView.
	const SLOP_WARNING =
		'WELCOME TO THE SLOP ZONE! All harnesses here are AI generated. ' +
		"I'll be working through human written replacements after I get back from vacation " +
		'\u{1F3DD}\u{FE0F}. They are worth going through if a specific topic interests you. ' +
		'I asked Claude to demonstrate ALL components. Happy tinkering!';

	/** Whether the experimental section is open. Closed on arrival — it is asked for each visit. */
	let showExperimental = $state(false);

	let experimental = $derived(presets.filter((p) => p.folder === EXPERIMENTAL_FOLDER));

	// The host's order is already folder-by-folder, so grouping is a single pass — no sorting here, or
	// the page and the library would disagree about precedence. The experimental folder is skipped:
	// it gets its own section below rather than a heading in this run.
	let groups = $derived.by(() => {
		const out: { folder: string; label: string; items: UiPreset[] }[] = [];
		for (const preset of presets) {
			if (preset.folder === EXPERIMENTAL_FOLDER) {
				continue;
			}
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
	<div class="mb-4 flex items-center justify-end">
		<Button variant="outline" size="sm" class="gap-1" onclick={onclose}>
			<ArrowLeftIcon class="size-4" />
			Go Back
		</Button>
	</div>

	<h2 class="text-lg font-semibold">Add a preset</h2>
	<p class="text-muted-foreground mt-1 text-sm">
		Load a ready-made Physalia pipeline. Pick one below and it arrives as a new harness on your
		canvas, ready to use — whatever is already there keeps running.
	</p>

	<!-- One preset row, shared by the gallery and the experimental section — they differ in what is
	     said ABOVE them, not in what a row is or does. -->
	{#snippet row(preset: UiPreset)}
		<!-- The row is only the description's ANCHOR; the panel itself is rendered once, below,
		     in fixed coordinates. Focus reveals it too, so the keyboard reaches it. -->
		<div
			role="presentation"
			onmouseenter={(event) => show(event, preset)}
			onmouseleave={hide}
			onfocusin={(event) => show(event, preset)}
			onfocusout={hide}
		>
			<Button
				variant="outline"
				class="h-auto w-full justify-start gap-2 py-2.5 text-left"
				onclick={() => onplace(preset.file)}
			>
				<PlusIcon class="size-4 shrink-0" />
				{preset.name}
			</Button>
		</div>
	{/snippet}

	{#each groups as group (group.folder)}
		<h3 class="text-muted-foreground mt-5 text-xs font-semibold tracking-wide uppercase">
			{group.label}
		</h3>

		<div class="mt-2 flex flex-col gap-2">
			{#each group.items as preset (preset.file)}
				{@render row(preset)}
			{/each}
		</div>
	{/each}

	{#if experimental.length > 0}
		<!-- Pink, and last. The button carries its own colour rather than a variant, because this is
		     the only place in the window that wears it: the Experimental pink is a label on these
		     harnesses, not a new button kind. aria-expanded both tells a screen reader what the button
		     does and gives .neu-btn its pressed-in look while the section is open. -->
		<div class="mt-6 flex flex-col gap-2">
			<Button
				variant="outline"
				class="h-auto w-full justify-start gap-2 bg-[var(--neu-experimental)] py-2.5 text-left text-[var(--neu-experimental-text)] hover:bg-[var(--neu-experimental-hover)]"
				aria-expanded={showExperimental}
				onclick={() => (showExperimental = !showExperimental)}
			>
				<FlaskConicalIcon class="size-4 shrink-0" />
				Experimental
				<span class="ml-auto flex items-center gap-1 text-xs font-normal opacity-80">
					{experimental.length}
					{#if showExperimental}
						<ChevronDownIcon class="size-4 shrink-0" />
					{:else}
						<ChevronRightIcon class="size-4 shrink-0" />
					{/if}
				</span>
			</Button>

			{#if showExperimental}
				<p
					class="rounded-lg bg-[var(--neu-experimental-soft)] px-3 py-2 text-xs text-[var(--neu-experimental-text)]"
				>
					{SLOP_WARNING}
				</p>

				{#each experimental as preset (preset.file)}
					{@render row(preset)}
				{/each}
			{/if}
		</div>
	{/if}

	{#if groups.length === 0 && experimental.length === 0}
		<div class="mt-6 flex flex-col items-center gap-4">
			<HappyFace />
			<p class="text-muted-foreground text-center text-sm">
				No presets found. Save one with <em>Save Harness as Preset</em> — on a Harness component's
				right-click menu, or on the <em>Harness</em> widget shown while you are inside one — and it
				will appear here under <em>Yours</em>.
			</p>
		</div>
	{/if}

	{#if tip}
		<div
			class="neu-popover text-foreground pointer-events-none fixed z-50 overflow-hidden rounded-md px-3 py-2 text-xs whitespace-pre-wrap"
			style:left="{tip.left}px"
			style:width="{tip.width}px"
			style:top={tip.top === null ? undefined : `${tip.top}px`}
			style:bottom={tip.bottom === null ? undefined : `${tip.bottom}px`}
			style:max-height="{tip.maxHeight}px"
		>
			{tip.text}
		</div>
	{/if}
</div>

<svelte:window onresize={follow} />
<svelte:document onscrollcapture={follow} />
