<script lang="ts">
	// The card a tool puts up when it wants permission before acting — a download, or unpacking an
	// archive. It replaced a Rhino message box, for two reasons.
	//
	//  1. An approval is part of a turn: the model asked for something, and the person is being asked
	//     whether it may have it. That belongs where the conversation is, next to what prompted it.
	//  2. A modal Rhino dialog is a window, and a window can end up behind Rhino, on another monitor,
	//     or over a canvas the user was not looking at.
	//
	// It sits ABOVE the composer rather than in the message stream, and that is deliberate: it is not
	// history, it is a thing to be answered, and it must stay put while the conversation scrolls. It
	// is also reachable from any page — the window may be on Home or a setup page when the model asks.
	//
	// The tool call is BLOCKED while this is up. Not answering is not neutral: the host denies after
	// five minutes, so the round ends either way, and the card says so rather than leaving the user to
	// discover it.
	import { answerApproval, type UiApproval } from '$lib/bridge';
	import CheckIcon from '@lucide/svelte/icons/check';
	import XIcon from '@lucide/svelte/icons/x';
	import ShieldAlertIcon from '@lucide/svelte/icons/shield-alert';

	interface Props {
		/** Questions waiting for an answer, oldest first. */
		approvals: UiApproval[];
	}

	let { approvals }: Props = $props();

	// Answered cards are cleared by the host's next push, but the round is waiting on this answer and
	// a button that stays live after a click invites a second one. Tracked per id so a queue of cards
	// does not disable itself wholesale.
	let answered = $state<Set<string>>(new Set());

	function answer(approval: UiApproval, allow: boolean) {
		if (answered.has(approval.id)) {
			return;
		}

		answered = new Set(answered).add(approval.id);
		answerApproval(approval.id, allow);
	}

	// Only the OLDEST is shown when several are waiting. Stacking consent prompts is how people learn
	// to clear them without reading, and the rest are still queued on the host.
	let current = $derived(approvals[0] ?? null);
	let waiting = $derived(Math.max(approvals.length - 1, 0));
</script>

{#if current}
	<div
		class="neu-raised mb-2 flex flex-col gap-2.5 rounded-xl p-3 text-left"
		role="alertdialog"
		aria-label={current.title}
	>
		<div class="flex items-start gap-2">
			<ShieldAlertIcon class="mt-0.5 size-4 shrink-0 text-[var(--neu-accent)]" />
			<div class="flex min-w-0 flex-col gap-0.5">
				<h2 class="text-sm font-semibold">{current.title}</h2>
				<p class="text-muted-foreground text-xs">{current.summary}</p>
			</div>
		</div>

		<!-- Verbatim, wrapping rather than truncating, and selectable: the URL and the destination ARE
		     the decision. break-all so a long path cannot push the card wider than the window. -->
		<div class="neu-well max-h-32 overflow-y-auto rounded-lg p-2.5 font-mono text-xs break-all whitespace-pre-wrap select-text">
			{current.detail}
		</div>

		{#if current.harness}
			<p class="text-muted-foreground text-[11px]">
				Asked by <span class="font-medium">{current.harness}</span>
			</p>
		{/if}

		<div class="flex items-center gap-2">
			{#if waiting > 0}
				<span class="text-muted-foreground text-[11px]">
					{waiting} more waiting
				</span>
			{/if}
			<button
				type="button"
				class="neu-btn text-muted-foreground hover:text-foreground ml-auto flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs disabled:opacity-50"
				disabled={answered.has(current.id)}
				onclick={() => answer(current, false)}
			>
				<XIcon class="size-3.5" />
				Deny
			</button>
			<button
				type="button"
				class="neu-btn flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs text-[var(--neu-accent)] disabled:opacity-50"
				disabled={answered.has(current.id)}
				onclick={() => answer(current, true)}
			>
				<CheckIcon class="size-3.5" />
				Allow
			</button>
		</div>

		<p class="text-muted-foreground text-[11px]">
			The pipeline is waiting on this. If nothing is chosen it is denied after five minutes.
		</p>
	</div>
{/if}
