// Stub for Shiki language grammars the chat UI never needs. streamdown-svelte
// statically references every bundled grammar via dynamic import; vite-plugin-singlefile
// then inlines them all (~13 MB). We alias every grammar except json/markdown to this
// empty module (see vite.config.ts). If a code fence in an unsupported language ever
// appears, Shiki's forgiving engine just renders it as plain text.
export default [];
