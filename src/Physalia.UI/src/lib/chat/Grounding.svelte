<script lang="ts">
	// Grounding selection page. Lets the user narrow which installed components are folded into the
	// system prompt. The top view lists a pill per available grounding kind (only "Components" today);
	// picking it drills into a two-level tree of GH tabs (categories) and panels (sub-categories).
	//
	// Selection semantics (opt-in, nullable): a null selection from the host means "include
	// everything" (the default) — every tree leaf renders checked. The first toggle materializes the
	// explicit set (everything minus the toggled leaf) and sends it back; "Reset to all" returns to
	// the null/include-everything default.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import LayersIcon from '@lucide/svelte/icons/layers';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import SquareIcon from '@lucide/svelte/icons/square';
	import SquareCheckIcon from '@lucide/svelte/icons/square-check';
	import SquareMinusIcon from '@lucide/svelte/icons/square-minus';
	import BoxIcon from '@lucide/svelte/icons/box';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import type {
		ClusterInfo,
		ClusterSelectionPayload,
		GroundingCategory,
		GroundingSelectionPayload
	} from '$lib/bridge';

	interface Props {
		/** Available tabs (categories) and their panels (sub-categories). */
		tree: GroundingCategory[];
		/** Current included tabs/panels, or null = include everything (default). */
		selection: GroundingCategory[] | null;
		/** Available clusters (from Files/CLUSTERS). */
		clusters: ClusterInfo[];
		/** Current included cluster names, or null = include everything (default). */
		clusterSelection: string[] | null;
		/** Applies a new component selection (host action). all=true returns to include-everything. */
		onapply: (payload: GroundingSelectionPayload) => void;
		/** Applies a new cluster selection (host action). all=true returns to include-everything. */
		onapplyclusters: (payload: ClusterSelectionPayload) => void;
		/** Returns to the chat view. */
		onclose: () => void;
	}

	let { tree, selection, clusters, clusterSelection, onapply, onapplyclusters, onclose }: Props =
		$props();

	// Two-level page: the kind pills, then a chosen kind's detail.
	let view = $state<'kinds' | 'components' | 'clusters'>('kinds');

	// Unit Separator (U+001F) packs a (category, subCategory) pair into one Set key. It is guaranteed
	// absent from any Grasshopper tab/panel name, so splitting the key back is unambiguous. A space
	// separator is fatal here: tab names and panel names contain spaces ("LAS Clouds", "Goals-6dof"),
	// so a first-space split mangles them.
	const SEP = String.fromCharCode(31);
	const leafKey = (category: string, subCategory: string) => `${category}${SEP}${subCategory}`;

	// Included-leaf set, the local source of truth. Initialised once from props: a null selection
	// means everything is included. Local edits drive the host (which echoes the same state back), so
	// we deliberately do not re-sync from props while the panel is open.
	function initialIncluded(): Set<string> {
		const set = new Set<string>();
		const source = selection ?? tree;
		for (const cat of source) {
			for (const sub of cat.subCategories) {
				set.add(leafKey(cat.category, sub));
			}
		}
		return set;
	}

	let included = $state(initialIncluded());
	let expanded = $state(new Set<string>());

	function categoryState(cat: GroundingCategory): 'all' | 'some' | 'none' {
		if (cat.subCategories.length === 0) {
			return 'none';
		}
		const on = cat.subCategories.filter((sub) => included.has(leafKey(cat.category, sub))).length;
		if (on === 0) {
			return 'none';
		}
		return on === cat.subCategories.length ? 'all' : 'some';
	}

	function applyToHost() {
		const leaves: [string, string][] = [];
		for (const key of included) {
			const idx = key.indexOf(SEP);
			if (idx < 0) {
				continue; // malformed key — skip rather than emit a mangled (category, subCategory) pair
			}
			leaves.push([key.slice(0, idx), key.slice(idx + 1)]);
		}
		onapply({ all: false, leaves });
	}

	function toggleLeaf(category: string, subCategory: string) {
		const key = leafKey(category, subCategory);
		const next = new Set(included);
		if (next.has(key)) {
			next.delete(key);
		} else {
			next.add(key);
		}
		included = next;
		applyToHost();
	}

	function toggleCategory(cat: GroundingCategory) {
		const turnOff = categoryState(cat) === 'all';
		const next = new Set(included);
		for (const sub of cat.subCategories) {
			const key = leafKey(cat.category, sub);
			if (turnOff) {
				next.delete(key);
			} else {
				next.add(key);
			}
		}
		included = next;
		applyToHost();
	}

	function toggleExpanded(category: string) {
		const next = new Set(expanded);
		if (next.has(category)) {
			next.delete(category);
		} else {
			next.add(category);
		}
		expanded = next;
	}

	// Restore the include-everything default: re-check every leaf locally and clear the host selection.
	function resetAll() {
		included = initialIncludedFromTree();
		onapply({ all: true, leaves: [] });
	}

	function initialIncludedFromTree(): Set<string> {
		const set = new Set<string>();
		for (const cat of tree) {
			for (const sub of cat.subCategories) {
				set.add(leafKey(cat.category, sub));
			}
		}
		return set;
	}

	let allIncluded = $derived(
		tree.length > 0 && tree.every((cat) => categoryState(cat) === 'all')
	);
	let includedCount = $derived(included.size);

	// Cluster selection: a flat set of included cluster names, the local source of truth. Initialised
	// once from props (null selection = everything), then local edits drive the host. As with the
	// component tree, we deliberately do not re-sync from props while the panel is open.
	function initialIncludedClusters(): Set<string> {
		return new Set(clusterSelection ?? clusters.map((c) => c.name));
	}

	let includedClusters = $state(initialIncludedClusters());

	function toggleCluster(name: string) {
		const next = new Set(includedClusters);
		if (next.has(name)) {
			next.delete(name);
		} else {
			next.add(name);
		}
		includedClusters = next;
		onapplyclusters({ all: false, names: [...next] });
	}

	// Restore the include-everything default: re-check every cluster locally and clear the selection.
	function resetAllClusters() {
		includedClusters = new Set(clusters.map((c) => c.name));
		onapplyclusters({ all: true, names: [] });
	}

	let allClustersIncluded = $derived(
		clusters.length > 0 && clusters.every((c) => includedClusters.has(c.name))
	);

	function clusterSignature(cluster: ClusterInfo): string {
		return `(in: ${cluster.inputs.join(', ') || '—'}) → (out: ${cluster.outputs.join(', ') || '—'})`;
	}
</script>

<div class="mx-auto flex w-full max-w-xl flex-col px-4 py-6">
	{#if view === 'kinds'}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={onclose}>
				<ArrowLeftIcon class="size-4" />
				Back to chat
			</Button>
		</div>

		<h2 class="text-lg font-semibold">Grounding</h2>
		<p class="text-muted-foreground mt-1 text-sm">
			Choose which available context is folded into the model's system prompt. Pick a category
			below to refine what's included.
		</p>

		{#if tree.length > 0 || clusters.length > 0}
			<div class="mt-4 flex flex-col gap-2">
				{#if tree.length > 0}
					<Button
						variant="outline"
						class="h-auto w-full justify-start gap-2 py-2.5 text-left"
						onclick={() => (view = 'components')}
					>
						<LayersIcon class="size-4 shrink-0" />
						<span class="flex-1">Components</span>
						<span class="text-muted-foreground text-xs">
							{allIncluded ? 'All included' : `${includedCount} panel(s)`}
						</span>
					</Button>
				{/if}
				{#if clusters.length > 0}
					<Button
						variant="outline"
						class="h-auto w-full justify-start gap-2 py-2.5 text-left"
						onclick={() => (view = 'clusters')}
					>
						<BoxIcon class="size-4 shrink-0" />
						<span class="flex-1">Clusters</span>
						<span class="text-muted-foreground text-xs">
							{allClustersIncluded ? 'All included' : `${includedClusters.size} of ${clusters.length}`}
						</span>
					</Button>
				{/if}
			</div>
		{:else}
			<div class="mt-6 flex flex-col items-center gap-4">
				<HappyFace />
				<p class="text-muted-foreground text-center text-sm">
					No grounding wired. Connect a <strong>Library</strong> (component catalog) or a
					<strong>Cluster Grounding</strong> to the Recorder's Grounding input to choose what's
					available.
				</p>
			</div>
		{/if}
	{:else if view === 'components'}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={() => (view = 'kinds')}>
				<ArrowLeftIcon class="size-4" />
				Grounding
			</Button>
			<Button variant="ghost" size="sm" onclick={resetAll} disabled={allIncluded}>
				Reset to all
			</Button>
		</div>

		<h2 class="text-lg font-semibold">Components</h2>
		<p class="text-muted-foreground mt-1 text-sm">
			Deselect a tab or panel to keep those components out of the system prompt. Everything is
			included by default.
		</p>

		<div class="mt-4 flex flex-col gap-1">
			{#each tree as cat (cat.category)}
				{@const state = categoryState(cat)}
				<div class="neu-raised-sm rounded-md">
					<div class="flex items-center gap-1 px-2 py-1.5">
						<button
							type="button"
							class="text-foreground/80 hover:text-foreground flex items-center"
							title={state === 'all' ? 'Deselect all panels' : 'Select all panels'}
							onclick={() => toggleCategory(cat)}
						>
							{#if state === 'all'}
								<SquareCheckIcon class="size-4" />
							{:else if state === 'some'}
								<SquareMinusIcon class="size-4" />
							{:else}
								<SquareIcon class="size-4" />
							{/if}
						</button>
						<button
							type="button"
							class="hover:bg-muted-foreground/10 flex flex-1 items-center gap-1 rounded px-1 py-0.5 text-left text-sm font-medium"
							onclick={() => toggleExpanded(cat.category)}
						>
							{#if expanded.has(cat.category)}
								<ChevronDownIcon class="size-3.5 shrink-0" />
							{:else}
								<ChevronRightIcon class="size-3.5 shrink-0" />
							{/if}
							<span class="flex-1">{cat.category}</span>
							<span class="text-muted-foreground text-xs">{cat.subCategories.length}</span>
						</button>
					</div>

					{#if expanded.has(cat.category)}
						<div class="flex flex-col gap-0.5 px-2 pb-2 pl-8">
							{#each cat.subCategories as sub (sub)}
								{@const on = included.has(leafKey(cat.category, sub))}
								<button
									type="button"
									class="hover:bg-muted-foreground/10 flex items-center gap-2 rounded px-1 py-1 text-left text-sm"
									onclick={() => toggleLeaf(cat.category, sub)}
								>
									{#if on}
										<SquareCheckIcon class="text-foreground/80 size-4 shrink-0" />
									{:else}
										<SquareIcon class="text-muted-foreground size-4 shrink-0" />
									{/if}
									<span>{sub}</span>
								</button>
							{/each}
						</div>
					{/if}
				</div>
			{/each}
		</div>
	{:else}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={() => (view = 'kinds')}>
				<ArrowLeftIcon class="size-4" />
				Grounding
			</Button>
			<Button variant="ghost" size="sm" onclick={resetAllClusters} disabled={allClustersIncluded}>
				Reset to all
			</Button>
		</div>

		<h2 class="text-lg font-semibold">Clusters</h2>
		<p class="text-muted-foreground mt-1 text-sm">
			Choose which Grasshopper clusters the model may use. Everything is included by default.
		</p>

		<div class="mt-4 flex flex-col gap-1">
			{#each clusters as cluster (cluster.name)}
				{@const on = includedClusters.has(cluster.name)}
				<button
					type="button"
					class="hover:bg-muted-foreground/10 flex items-start gap-2 rounded px-2 py-1.5 text-left text-sm"
					onclick={() => toggleCluster(cluster.name)}
				>
					{#if on}
						<SquareCheckIcon class="text-foreground/80 mt-0.5 size-4 shrink-0" />
					{:else}
						<SquareIcon class="text-muted-foreground mt-0.5 size-4 shrink-0" />
					{/if}
					<span class="flex min-w-0 flex-col">
						<span class="font-medium">{cluster.name}</span>
						{#if cluster.description}
							<span class="text-muted-foreground text-xs">{cluster.description}</span>
						{/if}
						<span class="text-muted-foreground/80 font-mono text-xs">
							{clusterSignature(cluster)}
						</span>
					</span>
				</button>
			{/each}
		</div>
	{/if}
</div>
