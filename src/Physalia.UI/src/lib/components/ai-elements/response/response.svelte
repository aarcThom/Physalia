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

	// streamdown's shadcn theme is written for a page, not a 460px panel: an h1 lands at text-3xl, a
	// table cell reserves min-w-[200px] (so a three-column table is 600px wide and scrolls inside a
	// window it would otherwise have fitted), and its surfaces are bordered cards rather than the
	// window's own soft wells. These overrides re-scale the type, let cells size to their content, and
	// put code blocks and tables on the app's neu-* tokens. Only what is listed changes — the theme is
	// merged over the base with tailwind-merge, so a conflicting utility replaces its counterpart and
	// everything unmentioned (thead tint, row rules, alerts, footnotes) stays as it ships.
	const chatTheme: StreamdownProps['theme'] = {
		h1: { base: 'mt-4 mb-1 text-lg' },
		h2: { base: 'mt-4 mb-1 text-base' },
		h3: { base: 'mt-3 mb-1 text-sm' },
		h4: { base: 'mt-3 mb-1 text-sm' },
		h5: { base: 'mt-3 mb-1 text-sm' },
		h6: { base: 'mt-3 mb-1 text-sm' },
		ul: { base: 'my-2 list-disc' },
		ol: { base: 'my-2 list-decimal' },
		li: { base: 'py-0.5' },
		link: { base: 'text-[var(--neu-accent)] underline-offset-2' },
		blockquote: { base: 'my-3 border-l-2 pl-3' },
		hr: { base: 'my-4' },
		codespan: { base: 'text-[0.85em]' },
		code: {
			base: 'neu-well my-3 gap-1 rounded-lg border-0 p-1.5',
			container: 'rounded-md border-0 bg-transparent p-2 text-xs',
			pre: 'bg-transparent',
			buttons: 'neu-btn border-0'
		},
		table: {
			wrapper: 'neu-well my-3 gap-1 rounded-lg border-0 p-1.5',
			container: 'rounded-md border-0 bg-transparent',
			toolbar: 'neu-btn border-0'
		},
		td: { base: 'min-w-0 max-w-none px-2 py-1 text-xs' },
		th: { base: 'min-w-0 max-w-none px-2 py-1 text-xs font-semibold' }
	};
	let currentTheme = $derived(
		mode.current === 'dark' ?  'github-dark-default' : 'github-light-default'
	);
</script>

<div class={cn('size-full [&>*:first-child]:mt-0 [&>*:last-child]:mb-0', className)}>
	<Streamdown
		{content}
		baseTheme="shadcn"
		theme={chatTheme}
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
