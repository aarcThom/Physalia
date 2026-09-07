<script lang="ts">
	// The Trigger Control page: every trigger in this pipeline, with a switch for each and one for all
	// of them.
	//
	// Why it exists at all: a trigger's own right-click menu is the correct home for arming ONE, and
	// stops being enough at three — they are scattered inside a harness the user is usually not looking
	// at, and arming is the act with a bill attached. Before this, "what is switched on right now" had
	// no answer short of visiting every node, and the harness panel could only say how many were armed
	// and switch them all off, which is a fire alarm rather than a control.
	//
	// The list is read live off the canvas on the window's own tick, not pushed on a change: arming
	// alters no data and runs no Grasshopper solution, so there is no event to push from.
	import { Button } from '$lib/components/ui/button/index.js';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import ZapIcon from '@lucide/svelte/icons/zap';
	import ZapOffIcon from '@lucide/svelte/icons/zap-off';
	import CircleAlertIcon from '@lucide/svelte/icons/circle-alert';
	import type { UiTrigger } from '$lib/bridge';

	interface Props {
		/** Every trigger in the pipeline, armed or not, in canvas order. */
		triggers: UiTrigger[];
		/** Arms or disarms one trigger — the same act as its own menu, so a recorder hands over. */
		onarm: (id: string, on: boolean) => void;
		/** Arms all, or switches all off. Switching all off discards a recorder's batch. */
		onarmall: (on: boolean) => void;
		/** Returns to the chat view. */
		onclose: () => void;
	}

	let { triggers, onarm, onarmall, onclose }: Props = $props();

	let armedCount = $derived(triggers.filter((t) => t.armed).length);
	let allArmed = $derived(triggers.length > 0 && armedCount === triggers.length);

	// A recorder that is armed is the one case where switching everything off loses something, so the
	// warning appears only then rather than sitting there permanently.
	let losesWork = $derived(triggers.some((t) => t.armed && t.handsOver));
</script>

<div class="mx-auto flex w-full max-w-xl flex-col px-4 py-6">
	<div class="mb-4 flex items-center justify-end">
		<Button variant="outline" size="sm" class="gap-1" onclick={onclose}>
			<ArrowLeftIcon class="size-4" />
			Go Back
		</Button>
	</div>

	<h2 class="text-lg font-semibold">Triggers</h2>
	<p class="text-muted-foreground mt-1 text-sm">
		What can start a round in this pipeline on its own. Everything here is off when a file opens —
		nothing runs on a machine nobody is watching — so arming is always something you do
		deliberately.
	</p>

	{#if triggers.length === 0}
		<div class="neu-well mt-6 rounded-xl p-4">
			<p class="text-sm font-medium">No triggers in this pipeline.</p>
			<p class="text-muted-foreground mt-1 text-xs">
				Add a Timer, Folder Watcher, Rhino Changed, Data Changed or Watch Modelling inside the
				harness and they will appear here.
			</p>
		</div>
	{:else}
		<div class="mt-5 flex items-center gap-2">
			<span class="text-muted-foreground text-xs">
				{armedCount} of {triggers.length} armed
			</span>
			<Button
				variant="outline"
				size="sm"
				class="ml-auto gap-1.5"
				disabled={allArmed}
				onclick={() => onarmall(true)}
			>
				<ZapIcon class="size-3.5" />
				Arm all
			</Button>
			<Button
				variant="outline"
				size="sm"
				class="gap-1.5"
				disabled={armedCount === 0}
				onclick={() => onarmall(false)}
			>
				<ZapOffIcon class="size-3.5" />
				Switch all off
			</Button>
		</div>

		<!-- Only while a recorder is actually armed. A permanent warning is one nobody reads. -->
		{#if losesWork}
			<p class="text-muted-foreground mt-2 flex items-start gap-1.5 text-[11px]">
				<CircleAlertIcon class="mt-0.5 size-3.5 shrink-0 text-[var(--neu-accent)]" />
				<span>
					"Switch all off" discards what a recording has gathered. To send a recording, switch that
					one off on its own row.
				</span>
			</p>
		{/if}

		<div class="mt-4 flex flex-col gap-2">
			{#each triggers as trigger (trigger.id)}
				<!-- The whole row is the switch. A trigger is one thing with one state, so a row with a
				     separate control on it invites the question of which part to press. -->
				<button
					type="button"
					class="neu-raised flex w-full items-center gap-3 rounded-xl p-3 text-left"
					onclick={() => onarm(trigger.id, !trigger.armed)}
					title={trigger.armed ? 'Switch this off' : 'Arm this trigger'}
				>
					<span
						class="flex size-8 shrink-0 items-center justify-center rounded-lg {trigger.armed
							? 'text-[var(--neu-accent)]'
							: 'text-muted-foreground'}"
					>
						{#if trigger.armed}
							<ZapIcon class="size-4" />
						{:else}
							<ZapOffIcon class="size-4" />
						{/if}
					</span>

					<span class="flex min-w-0 flex-1 flex-col">
						<span class="flex items-baseline gap-2">
							<span class="truncate text-sm font-medium">{trigger.kind}</span>
							<!-- Only when it has been renamed: repeating "Timer — Timer" is noise. -->
							{#if trigger.name && trigger.name !== trigger.kind}
								<span class="text-muted-foreground truncate text-xs">{trigger.name}</span>
							{/if}
						</span>

						<!-- The node's own caption, verbatim, so this page says what the canvas says. -->
						{#if trigger.caption}
							<span class="text-muted-foreground truncate text-xs">{trigger.caption}</span>
						{/if}

						{#if trigger.armed && trigger.handsOver}
							<span class="mt-0.5 text-[11px] text-[var(--neu-accent)]">
								Switching this off sends what it has recorded.
							</span>
						{/if}
					</span>

					<span
						class="shrink-0 text-[11px] {trigger.armed
							? 'text-[var(--neu-accent)]'
							: 'text-muted-foreground'}"
					>
						{trigger.armed ? 'ARMED' : 'off'}
					</span>
				</button>
			{/each}
		</div>
	{/if}
</div>
