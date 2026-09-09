<script lang="ts">
	// The build number, small, under the logo on the two entry screens (Setup and ConnectOptions).
	//
	// It is here rather than inside HappyFace.svelte because the critter is shared by six screens —
	// setup, connect, preset, grounding, MCP and API — and a version number belongs on the one the
	// user LANDS on, not on every page that happens to show the mark.
	//
	// Why show it at all: Rhino 8 updates package-manager plug-ins silently at startup, so the
	// version a user is running is something they otherwise have no way to see, and "which version
	// are you on?" is the first question any bug report needs. The trimmed form reads ("1.0"); the
	// full four-part build hangs off the tooltip, where it costs nothing and answers that question
	// exactly.
	import type { UiVersion } from '$lib/bridge';

	interface Props {
		/** The running build, or null before the host has pushed it (nothing renders then). */
		version: UiVersion | null;
	}

	let { version }: Props = $props();
</script>

{#if version}
	<p
		class="text-muted-foreground -mt-2 text-[11px] tabular-nums select-none"
		title="Physalia {version.full}"
	>
		v{version.display}
	</p>
{/if}
