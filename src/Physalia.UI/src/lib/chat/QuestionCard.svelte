<script lang="ts">
	// The card an Ask Human tool puts up when the model needs something only the person can supply.
	//
	// Sibling of ApprovalCard, and the differences are deliberate rather than incidental:
	//
	//  - An approval has two answers and both are one click. This has an answer that has to be
	//    composed, so it carries a text box, and it must not be answerable by accident.
	//  - An approval that goes unanswered is a No. This one cannot have a default at all — inventing
	//    an answer would have the model proceed as though a person had agreed to something — so the
	//    only thing the timeout can do is report that nobody replied, which is what Skip says too.
	//  - "Select in Rhino" has no answer in this window at all. The button asks the host to read what
	//    is selected AT THAT MOMENT, because the whole point is that the person goes and selects
	//    something after reading the question.
	//
	// Like the approval card it sits above the composer rather than in the message stream: it is not
	// history, it is a thing to be answered, and it must stay put while the conversation scrolls.
	import { answerQuestion, type UiQuestion } from '$lib/bridge';
	import MessageCircleQuestionIcon from '@lucide/svelte/icons/message-circle-question';
	import SendIcon from '@lucide/svelte/icons/send';
	import MousePointerClickIcon from '@lucide/svelte/icons/mouse-pointer-click';

	interface Props {
		/** Questions waiting for an answer, oldest first. */
		questions: UiQuestion[];
	}

	let { questions }: Props = $props();

	// Only the OLDEST is shown, for the reason the approval card shows one: a stack of prompts is how
	// people learn to clear them without reading. The rest are still queued on the host.
	let current = $derived(questions[0] ?? null);
	let waiting = $derived(Math.max(questions.length - 1, 0));

	// Per-question, so a queue does not carry one answer over to the next question — and so the draft
	// survives the host's re-pushes, which happen on the window's own tick.
	let drafts = $state<Record<string, string>>({});
	let answered = $state<Set<string>>(new Set());

	let draft = $derived(current ? (drafts[current.id] ?? '') : '');
	let isAnswered = $derived(current ? answered.has(current.id) : false);
	let selecting = $derived(current?.kind === 'rhino-selection');

	function setDraft(value: string) {
		if (current) {
			drafts = { ...drafts, [current.id]: value };
		}
	}

	function send(text: string, options?: { skip?: boolean }) {
		if (!current || answered.has(current.id)) {
			return;
		}

		// Marked before the call, not after: the round is waiting on this answer and a live button
		// invites a second click that would answer the NEXT question in the queue.
		answered = new Set(answered).add(current.id);
		answerQuestion(current.id, text, { skip: options?.skip, readSelection: selecting });
	}

	// Enter sends, Shift+Enter makes a new line — the composer's own convention, so the two text
	// boxes in this window do not behave differently.
	function onKeydown(event: KeyboardEvent) {
		if (event.key === 'Enter' && !event.shiftKey) {
			event.preventDefault();
			send(draft);
		}
	}
</script>

{#if current}
	<div
		class="neu-raised mb-2 flex flex-col gap-2.5 rounded-xl p-3 text-left"
		role="form"
		aria-label={current.title}
	>
		<div class="flex items-start gap-2">
			<MessageCircleQuestionIcon class="mt-0.5 size-4 shrink-0 text-[var(--neu-accent)]" />
			<div class="flex min-w-0 flex-col gap-0.5">
				<h2 class="text-sm font-semibold">{current.title}</h2>
				{#if current.harness}
					<p class="text-muted-foreground text-[11px]">
						Asked by <span class="font-medium">{current.harness}</span>
					</p>
				{/if}
			</div>
		</div>

		<!-- The question verbatim, wrapping and selectable. Prose rather than mono: unlike an approval's
		     URL this is a sentence, and it is the model talking. -->
		<p class="text-sm whitespace-pre-wrap select-text">{current.prompt}</p>

		{#if selecting}
			<p class="text-muted-foreground text-xs">
				Select the objects you mean in Rhino, then press Send selection. Whatever is selected at that
				moment is what goes back.
			</p>
		{:else if current.choices.length > 0}
			<!-- Buttons for the model's guesses, and the text box stays: "none of those, because…" is
			     the answer a wrong guess needs, and removing it is how a wrong guess becomes a wrong
			     answer. -->
			<div class="flex flex-wrap gap-1.5">
				{#each current.choices as choice (choice)}
					<button
						type="button"
						class="neu-btn rounded-lg px-2.5 py-1.5 text-xs disabled:opacity-50"
						disabled={isAnswered}
						onclick={() => send(choice)}
					>
						{choice}
					</button>
				{/each}
			</div>
		{/if}

		{#if !selecting}
			<textarea
				class="neu-well max-h-40 min-h-16 resize-y rounded-lg p-2.5 text-xs select-text focus:outline-none"
				placeholder="Type your answer…"
				disabled={isAnswered}
				value={draft}
				oninput={(e) => setDraft((e.currentTarget as HTMLTextAreaElement).value)}
				onkeydown={onKeydown}
			></textarea>
		{/if}

		<div class="flex items-center gap-2">
			{#if waiting > 0}
				<span class="text-muted-foreground text-[11px]">
					{waiting} more waiting
				</span>
			{/if}
			<button
				type="button"
				class="neu-btn text-muted-foreground hover:text-foreground ml-auto rounded-lg px-3 py-1.5 text-xs disabled:opacity-50"
				disabled={isAnswered}
				onclick={() => send('', { skip: true })}
			>
				Skip
			</button>
			<button
				type="button"
				class="neu-btn flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs text-[var(--neu-accent)] disabled:opacity-50"
				disabled={isAnswered}
				onclick={() => send(draft)}
			>
				{#if selecting}
					<MousePointerClickIcon class="size-3.5" />
					Send selection
				{:else}
					<SendIcon class="size-3.5" />
					Send
				{/if}
			</button>
		</div>

		<p class="text-muted-foreground text-[11px]">
			The pipeline is waiting on this. If nothing is sent it is reported as unanswered after ten
			minutes — which the model is told, and will not read as agreement.
		</p>
	</div>
{/if}
