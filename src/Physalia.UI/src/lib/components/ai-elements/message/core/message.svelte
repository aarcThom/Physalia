<script lang="ts">
	import { cn } from "$lib/utils";
	import type { Snippet } from "svelte";
	import type { HTMLAttributes } from "svelte/elements";
	import type { MessageRole } from "../context/message-context.svelte.js";

	interface Props extends HTMLAttributes<HTMLDivElement> {
		from: MessageRole;
		/** When true, this user turn is auto-generated feedback — styled apart from typed input. */
		feedback?: boolean;
		class?: string;
		children: Snippet;
	}

	let { from, feedback = false, class: className, children, ...restProps }: Props = $props();
	// indexing
</script>

<div
	class={cn(
		"group flex w-full max-w-[95%] flex-col gap-2",
		from === "user"
			? feedback
				? "is-feedback ml-auto justify-end"
				: "is-user ml-auto justify-end"
			: "is-assistant",
		className
	)}
	data-role={from}
	{...restProps}
>
	{@render children()}
</div>
