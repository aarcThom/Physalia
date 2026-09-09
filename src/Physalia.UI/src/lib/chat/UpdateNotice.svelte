<script lang="ts">
	// Shown once, over everything, when Physalia has been updated behind the user's back.
	//
	// WHY IT EXISTS. Rhino 8's Package Manager updates installed plug-ins silently at startup — no
	// prompt, no notification, nothing in the interface to say it happened. That is a fine default
	// for a bug fix and a bad surprise for anything else: a pipeline can behave differently from how
	// it behaved yesterday, for a reason the user never agreed to and has no way to discover. So the
	// host records which build ran last and hands over a notice when it changes.
	//
	// It is a MODAL rather than a line in the conversation, and that is the one design decision here
	// worth defending: the user is about to run a pipeline that costs money against a build they did
	// not choose to install, and a banner they can scroll past does not tell them that in time.
	//
	// Dismissing goes back to the HOST (acknowledgeUpdate), which is what stops it returning. The
	// page never decides it has been seen — Rhino often starts with no chat window open, and a
	// notice pushed to nobody must not count as delivered.
	//
	// Shape and manners are LinkPrompt's, deliberately: same backdrop, same card, Escape dismisses.
	import { acknowledgeUpdate } from '$lib/bridge';
	import type { UiUpdateNotice } from '$lib/bridge';
	import { Button } from '$lib/components/ui/button/index.js';
	import Response from '$lib/components/ai-elements/response/response.svelte';

	interface Props {
		/** The notice, or null when there is nothing to report (nothing renders then). */
		notice: UiUpdateNotice | null;
		/** Clears it locally, so the dialog goes the moment it is dismissed. */
		onclose: () => void;
	}

	let { notice, onclose }: Props = $props();

	function dismiss(again: boolean) {
		acknowledgeUpdate(again);
		onclose();
	}

	// Escape dismisses, as everywhere else in this window — and counts as read, because the user
	// deliberately closed it.
	$effect(() => {
		if (!notice) {
			return;
		}

		const onKey = (event: KeyboardEvent) => {
			if (event.key === 'Escape') {
				event.stopPropagation();
				dismiss(true);
			}
		};

		document.addEventListener('keydown', onKey);
		return () => document.removeEventListener('keydown', onKey);
	});
</script>

{#if notice}
	<div
		class="bg-background/80 fixed inset-0 z-50 flex items-center justify-center p-4 backdrop-blur-sm"
		role="presentation"
	>
		<div
			class="neu-popover text-foreground flex max-h-full w-full max-w-md flex-col gap-3 overflow-hidden rounded-lg p-4"
			role="dialog"
			aria-modal="true"
			aria-labelledby="update-notice-title"
		>
			<h2 id="update-notice-title" class="text-sm font-medium">Physalia updated!</h2>

			<!-- Short and positive, by request. The version pair is the whole announcement; the only
			     other thing worth a sentence is where the user's own work is, because an update
			     replaces the plug-in's directory and "my harness is gone" is the fear it causes. The
			     WHAT is left to the changelog section below, which says it per release. -->
			<p class="text-muted-foreground text-xs leading-relaxed">
				You're on <span class="tabular-nums">{notice.to}</span>, up from
				<span class="tabular-nums">{notice.from}</span>.
				{#if notice.folder}
					Your harnesses, presets, prompts and memories are kept in
					<span class="text-foreground break-all select-all">{notice.folder}</span> — an update
					never touches them.
				{/if}
			</p>

			{#if notice.notes}
				<!-- The scroller is the notes, not the card: the heading and the buttons stay put on a
				     long release, so the dismiss action is never the thing you have to scroll to find. -->
				<div class="chat-scroll min-h-0 flex-1 overflow-y-auto pr-1 text-xs">
					<Response content={notice.notes} class="text-xs" />
				</div>
			{/if}

			<div class="flex items-center justify-between gap-2">
				<button
					type="button"
					class="text-muted-foreground hover:text-foreground text-[11px] underline-offset-2 hover:underline"
					onclick={() => dismiss(false)}
				>
					Don't tell me about updates
				</button>

				<Button variant="outline" size="sm" onclick={() => dismiss(true)}>Got it</Button>
			</div>
		</div>
	</div>
{/if}
