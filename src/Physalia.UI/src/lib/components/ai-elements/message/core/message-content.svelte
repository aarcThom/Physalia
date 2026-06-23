<script lang="ts">
	import { cn } from "$lib/utils";
	import type { Snippet } from "svelte";
	import type { HTMLAttributes } from "svelte/elements";

	interface Props extends HTMLAttributes<HTMLDivElement> {
		class?: string;
		children: Snippet;
	}

	let { class: className, children, ...restProps }: Props = $props();
</script>

<div
	class={cn(
		"flex w-fit max-w-full min-w-0 flex-col gap-2 text-sm",
		"group-[.is-user]:bg-[var(--neu-bubble)] group-[.is-user]:[box-shadow:var(--neu-shadow)] group-[.is-user]:text-[#2f0857] group-[.is-user]:ml-auto group-[.is-user]:rounded-lg group-[.is-user]:px-4 group-[.is-user]:py-3",
		// Feedback (auto-generated, not human-typed) user turns: same bubble layout, pink palette.
		"group-[.is-feedback]:bg-[var(--neu-feedback)] group-[.is-feedback]:[box-shadow:var(--neu-feedback-shadow)] group-[.is-feedback]:text-[var(--neu-feedback-text)] group-[.is-feedback]:ml-auto group-[.is-feedback]:rounded-lg group-[.is-feedback]:px-4 group-[.is-feedback]:py-3",
		"group-[.is-assistant]:text-foreground",
		className
	)}
	{...restProps}
>
	{@render children()}
</div>
