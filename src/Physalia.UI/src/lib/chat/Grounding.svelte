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
	import {
		DropdownMenu,
		DropdownMenuTrigger,
		DropdownMenuContent,
		DropdownMenuItem
	} from '$lib/components/ui/dropdown-menu/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import LayersIcon from '@lucide/svelte/icons/layers';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import SquareIcon from '@lucide/svelte/icons/square';
	import SquareCheckIcon from '@lucide/svelte/icons/square-check';
	import SquareMinusIcon from '@lucide/svelte/icons/square-minus';
	import BoxIcon from '@lucide/svelte/icons/box';
	import ImagePlusIcon from '@lucide/svelte/icons/image-plus';
	import Axis3dIcon from '@lucide/svelte/icons/axis-3d';
	import RulerIcon from '@lucide/svelte/icons/ruler';
	import WrenchIcon from '@lucide/svelte/icons/wrench';
	import ShapesIcon from '@lucide/svelte/icons/shapes';
	import CodeIcon from '@lucide/svelte/icons/code';
	import HappyFace from '$lib/chat/HappyFace.svelte';
	import type {
		ClusterInfo,
		ClusterSelectionPayload,
		GroundingCategory,
		GroundingSelectionPayload,
		PythonFunctionInfo,
		ReferencedGeometryInfo,
		SnapshotMessagePayload,
		ToolsSelectionPayload,
		UnitsOverridePayload
	} from '$lib/bridge';

	interface Props {
		/** Available tabs (categories) and their panels (sub-categories). */
		tree: GroundingCategory[];
		/** Current included tabs/panels, or null = include everything (default). */
		selection: GroundingCategory[] | null;
		/** True when typed component signatures are folded into the prompt instead of bare names. */
		exposeSignatures: boolean;
		/** Available clusters (from Files/CLUSTERS). */
		clusters: ClusterInfo[];
		/** Current included cluster names, or null = include everything (default). */
		clusterSelection: string[] | null;
		/** Tool names currently on the canvas (from the Tools Present grounder). */
		tools: string[];
		/** Current enabled tool names, or null = include everything (default). */
		toolsSelection: string[] | null;
		/** Parameters on the canvas referencing live Rhino geometry (read-only page). */
		referencedGeometry: ReferencedGeometryInfo[];
		/** Python functions available to the model (read-only page). */
		pythonFunctions: PythonFunctionInfo[];
		/** True when a document-units grounding is wired (shows the Document Units pill). */
		unitsWired: boolean;
		/** The active Rhino document's current unit system. */
		documentUnits: string;
		/** Current units override, or null = use the live document units (default). */
		unitsOverride: string | null;
		/** Unit-system choices for the dropdown (includes the current doc value + any override). */
		unitOptions: string[];
		/** True when a Geometry Snapshot human tool is wired (shows its pill in the Human Tools section). */
		snapshotWired: boolean;
		/** True when the tool sends its snapshot as its own message carrying the text below; false when
		 *  it attaches the snapshot to the prompt box instead, which makes that text dead — the page
		 *  becomes a read-only row. */
		snapshotSendsMessage: boolean;
		/** The tool's default message sent alongside the snapshot image. */
		snapshotDefaultMessage: string;
		/** Current snapshot-message override, or null = use the tool's default (default). */
		snapshotMessage: string | null;
		/** True when an Add Image human tool is wired (shows its row in the Human Tools section). */
		imageToolWired: boolean;
		/** Applies a new component selection (host action). all=true returns to include-everything. */
		onapply: (payload: GroundingSelectionPayload) => void;
		/** Toggles typed component signatures in the grounded system prompt (host action). */
		onapplysignatures: (on: boolean) => void;
		/** Applies a new cluster selection (host action). all=true returns to include-everything. */
		onapplyclusters: (payload: ClusterSelectionPayload) => void;
		/** Applies a new tools selection (host action). all=true returns to include-everything. */
		onapplytools: (payload: ToolsSelectionPayload) => void;
		/** Applies a document-units override (host action). reset=true returns to the live doc units. */
		onapplyunits: (payload: UnitsOverridePayload) => void;
		/** Applies a snapshot-message override (host action). reset=true returns to the grounding's default. */
		onapplysnapshot: (payload: SnapshotMessagePayload) => void;
		/** Switches the snapshot between send-as-its-own-message and attach-to-the-prompt (host action).
		 *  The flag lives on the Geometry Snapshot component, so the new value returns via props. */
		onapplysnapshotsends: (on: boolean) => void;
		/** Returns to the chat view. */
		onclose: () => void;
	}

	let {
		tree,
		selection,
		exposeSignatures,
		clusters,
		clusterSelection,
		tools,
		toolsSelection,
		referencedGeometry,
		pythonFunctions,
		unitsWired,
		documentUnits,
		unitsOverride,
		unitOptions,
		snapshotWired,
		snapshotSendsMessage,
		snapshotDefaultMessage,
		snapshotMessage,
		imageToolWired,
		onapply,
		onapplysignatures,
		onapplyclusters,
		onapplytools,
		onapplyunits,
		onapplysnapshot,
		onapplysnapshotsends,
		onclose
	}: Props = $props();

	// Two-level page: the kind pills, then a chosen kind's detail.
	let view = $state<
		'kinds' | 'components' | 'clusters' | 'tools' | 'canvas' | 'python' | 'units' | 'snapshot'
	>('kinds');

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

	// Expose-signatures toggle, the local source of truth. Initialised once from props like the
	// component tree; local edits drive the host (which echoes the same state back).
	let signaturesOn = $state(exposeSignatures);

	function toggleSignatures() {
		signaturesOn = !signaturesOn;
		onapplysignatures(signaturesOn);
	}

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

	// Tools selection: a flat set of enabled tool names, mirroring clusters. Initialised once from props
	// (null selection = every present tool enabled); local edits drive the host. Disabling a tool keeps
	// it on the canvas but stops it being advertised to the model.
	function initialIncludedTools(): Set<string> {
		return new Set(toolsSelection ?? tools);
	}

	let includedTools = $state(initialIncludedTools());

	function toggleTool(name: string) {
		const next = new Set(includedTools);
		if (next.has(name)) {
			next.delete(name);
		} else {
			next.add(name);
		}
		includedTools = next;
		onapplytools({ all: false, names: [...next] });
	}

	function resetAllTools() {
		includedTools = new Set(tools);
		onapplytools({ all: true, names: [] });
	}

	let allToolsIncluded = $derived(tools.length > 0 && tools.every((t) => includedTools.has(t)));

	// Document units: the value shown to the model is the override when set, else the live document
	// units. Selecting the document's own value clears the override so it keeps tracking the document;
	// any other choice overrides the text handed to the model (never the document itself).
	let effectiveUnits = $derived(unitsOverride ?? documentUnits);

	function selectUnit(units: string) {
		if (units === documentUnits) {
			onapplyunits({ reset: true, units: '' });
		} else {
			onapplyunits({ reset: false, units });
		}
	}

	function resetUnits() {
		onapplyunits({ reset: true, units: '' });
	}

	// Geometry-snapshot message: the text sent alongside the snapshot image is the override when
	// set, else the tool's default. Local source of truth, initialised once from props like
	// the other kinds; edits drive the host on change/blur. Clearing the box (or typing the default
	// verbatim) resets the override so the message keeps tracking the tool's default.
	let snapshotText = $state(snapshotMessage ?? snapshotDefaultMessage);
	let snapshotOverridden = $state(snapshotMessage !== null);

	function applySnapshotText() {
		const trimmed = snapshotText.trim();
		if (trimmed.length === 0 || trimmed === snapshotDefaultMessage.trim()) {
			snapshotText = snapshotDefaultMessage;
			snapshotOverridden = false;
			onapplysnapshot({ reset: true, message: '' });
		} else {
			snapshotOverridden = true;
			onapplysnapshot({ reset: false, message: snapshotText });
		}
	}

	function resetSnapshotMessage() {
		snapshotText = snapshotDefaultMessage;
		snapshotOverridden = false;
		onapplysnapshot({ reset: true, message: '' });
	}

	// Flips the tool between sending the snapshot as its own message and attaching it to the prompt box.
	// Fire-and-forget: the flag lives on the Geometry Snapshot component (the canvas context menu shows
	// the same state), so the new value arrives back through snapshotSendsMessage on the next push
	// rather than being mirrored locally — no local copy means no way for the two to disagree.
	function toggleSnapshotSends() {
		onapplysnapshotsends(!snapshotSendsMessage);
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

		{#if tree.length > 0 || clusters.length > 0 || tools.length > 0 || referencedGeometry.length > 0 || pythonFunctions.length > 0 || unitsWired || snapshotWired || imageToolWired}
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
				{#if tools.length > 0}
					<Button
						variant="outline"
						class="h-auto w-full justify-start gap-2 py-2.5 text-left"
						onclick={() => (view = 'tools')}
					>
						<WrenchIcon class="size-4 shrink-0" />
						<span class="flex-1">LLM Tools</span>
						<span class="text-muted-foreground text-xs">
							{allToolsIncluded ? 'All enabled' : `${includedTools.size} of ${tools.length}`}
						</span>
					</Button>
				{/if}
				{#if referencedGeometry.length > 0}
					<Button
						variant="outline"
						class="h-auto w-full justify-start gap-2 py-2.5 text-left"
						onclick={() => (view = 'canvas')}
					>
						<ShapesIcon class="size-4 shrink-0" />
						<span class="flex-1">Referenced Rhino Geometry</span>
						<span class="text-muted-foreground text-xs">{referencedGeometry.length}</span>
					</Button>
				{/if}
				{#if pythonFunctions.length > 0}
					<Button
						variant="outline"
						class="h-auto w-full justify-start gap-2 py-2.5 text-left"
						onclick={() => (view = 'python')}
					>
						<CodeIcon class="size-4 shrink-0" />
						<span class="flex-1">Python Functions</span>
						<span class="text-muted-foreground text-xs">{pythonFunctions.length}</span>
					</Button>
				{/if}
				{#if unitsWired}
					<Button
						variant="outline"
						class="h-auto w-full justify-start gap-2 py-2.5 text-left"
						onclick={() => (view = 'units')}
					>
						<RulerIcon class="size-4 shrink-0" />
						<span class="flex-1">Document Units</span>
						<span class="text-muted-foreground text-xs">
							{effectiveUnits || 'None'}{unitsOverride !== null ? ' (overridden)' : ''}
						</span>
					</Button>
				{/if}
			</div>

			<!-- Human tools sit apart from the grounding kinds: they are affordances for the human in
			     this window (a geometry button, image attachments), never folded into the prompt or
			     advertised to the model. Each appears only while its component is wired into the
			     Conversation Log's Human Tools input. -->
			{#if snapshotWired || imageToolWired}
				<div class="border-muted-foreground/20 mt-5 border-t pt-4">
					<h3 class="text-sm font-semibold">Human Tools</h3>
					<p class="text-muted-foreground mt-1 text-xs">
						Affordances for you in this chat window — never sent to the model.
					</p>
					<div class="mt-3 flex flex-col gap-2">
						{#if snapshotWired}
							<Button
								variant="outline"
								class="h-auto w-full justify-start gap-2 py-2.5 text-left"
								onclick={() => (view = 'snapshot')}
							>
								<Axis3dIcon class="size-4 shrink-0" />
								<span class="flex-1">Geometry Snapshot</span>
								<span class="text-muted-foreground text-xs">
									{!snapshotSendsMessage
										? 'Attaches to prompt'
										: snapshotOverridden
											? 'Custom message'
											: 'Default message'}
								</span>
							</Button>
						{/if}
						{#if imageToolWired}
							<!-- Read-only: the Add Image tool has nothing to configure — images ride the
							     prompt itself. The row just confirms the affordance is enabled. -->
							<div
								class="neu-raised-sm flex items-center gap-2 rounded-md px-3 py-2.5 text-sm"
								title="Images can be pasted, dragged in, or picked from disk in the prompt box"
							>
								<ImagePlusIcon class="text-muted-foreground size-4 shrink-0" />
								<span class="flex-1">Image attachments</span>
								<span class="text-muted-foreground text-xs">Enabled</span>
							</div>
						{/if}
					</div>
				</div>
			{/if}
		{:else}
			<div class="mt-6 flex flex-col items-center gap-4">
				<HappyFace />
				<p class="text-muted-foreground text-center text-sm">
					No grounding wired. Connect a <strong>Component Catalog</strong> or a
					<strong>Cluster Grounding</strong> to the Conversation Log's Grounding input to choose what's
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

		<button
			type="button"
			class="neu-raised-sm hover:bg-muted-foreground/10 mt-3 flex items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm"
			title="Folds each included component's full input/output signature (parameter nicknames and types) into the system prompt instead of just its name. This makes the prompt much larger — useful for models without tool calling but with large context windows. Prefer the search_components tool when tool calling is available."
			onclick={toggleSignatures}
		>
			{#if signaturesOn}
				<SquareCheckIcon class="text-foreground/80 size-4 shrink-0" />
			{:else}
				<SquareIcon class="text-muted-foreground size-4 shrink-0" />
			{/if}
			<span class="flex-1">Expose component signatures</span>
		</button>

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
	{:else if view === 'clusters'}
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
	{:else if view === 'tools'}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={() => (view = 'kinds')}>
				<ArrowLeftIcon class="size-4" />
				Grounding
			</Button>
			<Button variant="ghost" size="sm" onclick={resetAllTools} disabled={allToolsIncluded}>
				Reset to all
			</Button>
		</div>

		<h2 class="text-lg font-semibold">LLM Tools</h2>
		<p class="text-muted-foreground mt-1 text-sm">
			Choose which of the tools on the canvas the model may call. Everything is enabled by default;
			disabling a tool keeps it on the canvas but hides it from the model.
		</p>

		<div class="mt-4 flex flex-col gap-1">
			{#each tools as tool (tool)}
				{@const on = includedTools.has(tool)}
				<button
					type="button"
					class="hover:bg-muted-foreground/10 flex items-center gap-2 rounded px-2 py-1.5 text-left text-sm"
					onclick={() => toggleTool(tool)}
				>
					{#if on}
						<SquareCheckIcon class="text-foreground/80 size-4 shrink-0" />
					{:else}
						<SquareIcon class="text-muted-foreground size-4 shrink-0" />
					{/if}
					<span class="flex-1 font-mono">{tool}</span>
				</button>
			{/each}
		</div>
	{:else if view === 'canvas'}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={() => (view = 'kinds')}>
				<ArrowLeftIcon class="size-4" />
				Grounding
			</Button>
		</div>

		<h2 class="text-lg font-semibold">Referenced Rhino Geometry</h2>
		<p class="text-muted-foreground mt-1 text-sm">
			Parameters on the canvas that reference live geometry in the Rhino model. The model sees them
			in the canvas state and wires from them as data sources instead of recreating the geometry.
		</p>

		<div class="mt-4 flex flex-col gap-1">
			{#each referencedGeometry as input (input.name)}
				<div class="flex items-center gap-2 rounded px-2 py-1.5 text-sm">
					<ShapesIcon class="text-muted-foreground size-4 shrink-0" />
					<span class="flex-1 font-mono">{input.name}</span>
					<span class="text-muted-foreground text-xs">{input.type}</span>
				</div>
			{/each}
		</div>
	{:else if view === 'python'}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={() => (view = 'kinds')}>
				<ArrowLeftIcon class="size-4" />
				Grounding
			</Button>
		</div>

		<h2 class="text-lg font-semibold">Python Functions</h2>
		<p class="text-muted-foreground mt-1 text-sm">
			Python functions made available to the model to use where they fit.
		</p>

		<div class="mt-4 flex flex-col gap-2">
			{#each pythonFunctions as fn, i (i)}
				<div class="neu-raised-sm flex flex-col gap-1 rounded-md px-3 py-2">
					<span class="font-mono text-sm">{fn.signature}</span>
					{#if fn.docstring}
						<span class="text-muted-foreground text-xs">{fn.docstring}</span>
					{/if}
				</div>
			{/each}
		</div>
	{:else if view === 'snapshot'}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={() => (view = 'kinds')}>
				<ArrowLeftIcon class="size-4" />
				Grounding
			</Button>
			<Button
				variant="ghost"
				size="sm"
				onclick={resetSnapshotMessage}
				disabled={!snapshotOverridden || !snapshotSendsMessage}
			>
				Reset to default
			</Button>
		</div>

		<h2 class="text-lg font-semibold">Geometry Snapshot</h2>
		<p class="text-muted-foreground mt-1 text-sm">
			While geometry generated by a transmitter is on the canvas, the prompt box shows a geometry
			button. Pressing it captures a snapshot of the Rhino viewport, framed on that geometry.
		</p>

		<!-- The same toggle as the component's "Send With Default Message" context-menu item; the flag
		     lives there, so flipping it here also moves the checkmark on the canvas. -->
		<button
			type="button"
			class="neu-raised-sm hover:bg-muted-foreground/10 mt-3 flex items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm"
			title="On: the snapshot is sent immediately as its own message, carrying the text below. Off: the snapshot is attached to the prompt box like a pasted image, for you to write your own message."
			onclick={toggleSnapshotSends}
		>
			{#if snapshotSendsMessage}
				<SquareCheckIcon class="text-foreground/80 size-4 shrink-0" />
			{:else}
				<SquareIcon class="text-muted-foreground size-4 shrink-0" />
			{/if}
			<span class="flex-1">Send with default message</span>
		</button>

		<div class="mt-4 flex flex-col gap-2">
			<p class="text-muted-foreground text-sm">
				{#if snapshotSendsMessage}
					Edit the text that accompanies the snapshot below — it is sent as its own message, so
					nothing is ever attached to a prompt you typed.
				{:else}
					The snapshot is attached to your prompt instead, so you write the message yourself. The
					text below is unused until you switch sending back on.
				{/if}
			</p>

			<textarea
				bind:value={snapshotText}
				onchange={applySnapshotText}
				rows="5"
				disabled={!snapshotSendsMessage}
				placeholder={snapshotDefaultMessage}
				class="neu-well text-foreground w-full resize-y rounded-lg p-3 text-sm focus:outline-none disabled:opacity-50"
			></textarea>

			{#if snapshotOverridden && snapshotSendsMessage}
				<p class="text-muted-foreground text-xs">
					Overriding the default snapshot message. Clear the box (or reset) to return to the
					default.
				</p>
			{/if}
		</div>
	{:else}
		<div class="mb-4 flex items-center justify-between">
			<Button variant="ghost" size="sm" class="-ml-2 gap-1" onclick={() => (view = 'kinds')}>
				<ArrowLeftIcon class="size-4" />
				Grounding
			</Button>
			<Button variant="ghost" size="sm" onclick={resetUnits} disabled={unitsOverride === null}>
				Reset to document
			</Button>
		</div>

		<h2 class="text-lg font-semibold">Document Units</h2>
		<p class="text-muted-foreground mt-1 text-sm">
			The model is told the document's unit system so its numbers and geometry match. Override the
			value below to send different units to the model — this never changes the Rhino document.
		</p>

		<div class="mt-4 flex flex-col gap-3">
			<div class="text-sm">
				<span class="text-muted-foreground">Document units:</span>
				<span class="font-medium">{documentUnits || 'None'}</span>
			</div>

			<DropdownMenu>
				<DropdownMenuTrigger>
					{#snippet child({ props })}
						<Button variant="outline" class="w-full justify-between gap-2" {...props}>
							<span>{effectiveUnits || 'None'}</span>
							<ChevronDownIcon class="size-4 shrink-0" />
						</Button>
					{/snippet}
				</DropdownMenuTrigger>
				<DropdownMenuContent class="w-[--bits-dropdown-menu-anchor-width]">
					{#each unitOptions as option (option)}
						<DropdownMenuItem onSelect={() => selectUnit(option)}>
							<span class="flex-1">{option}</span>
							{#if option === documentUnits}
								<span class="text-muted-foreground text-xs">document</span>
							{/if}
						</DropdownMenuItem>
					{/each}
				</DropdownMenuContent>
			</DropdownMenu>

			{#if unitsOverride !== null}
				<p class="text-muted-foreground text-xs">
					Overriding the document's units ({documentUnits || 'None'}) with
					<span class="font-medium">{unitsOverride}</span> for the model only.
				</p>
			{/if}
		</div>
	{/if}
</div>
