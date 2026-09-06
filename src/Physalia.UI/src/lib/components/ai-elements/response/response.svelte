<script lang="ts">
	import { Streamdown, type StreamdownProps } from 'streamdown-svelte';
	// Add plugins as needed 
	// pnpm add @streamdown-svelte/code @streamdown-svelte/mermaid @streamdown-svelte/math @streamdown-svelte/cjk
	// import { code } from '@streamdown-svelte/code';
	// import { mermaid } from '@streamdown-svelte/mermaid';
	// import { math } from '@streamdown-svelte/math';
	// import { cjk } from '@streamdown-svelte/cjk';
	// import 'katex/dist/katex.min.css';

	import { mode } from 'mode-watcher';
	import LinkPrompt from '$lib/chat/LinkPrompt.svelte';
	import type { LinkSafetyModalProps } from 'streamdown-svelte';
	import githubDarkDefault from '@shikijs/themes/github-dark-default';
	import githubLightDefault from '@shikijs/themes/github-light-default';
	import { cn } from '$lib/utils';
	type Props = StreamdownProps;

	let { content, class: className, components, ...restProps }: Props = $props();
	let currentTheme = $derived(
		mode.current === 'dark' ?  'github-dark-default' : 'github-light-default'
	);
</script>

<div class={cn('size-full [&>*:first-child]:mt-0 [&>*:last-child]:mb-0', className)}>
	<Streamdown
		{content}
		baseTheme="shadcn"
		shikiTheme={currentTheme}
		shikiThemes={{
			'github-light-default': githubLightDefault,
			'github-dark-default': githubDarkDefault
		}}
		// plugins={{ code, mermaid, math, cjk }}
		linkSafety={{ enabled: true, renderModal: linkPrompt }}
		{...restProps}
	/>
</div>

<!-- Streamdown intercepts every external link and asks before following it; this is the dialog it
     asks with. Its own one is unusable here (see LinkPrompt.svelte), so it gets ours — which sends
     the link out through the host rather than calling window.open. -->
{#snippet linkPrompt(props: LinkSafetyModalProps)}
	<LinkPrompt url={props.url} isOpen={props.isOpen} onClose={props.onClose} />
{/snippet}
