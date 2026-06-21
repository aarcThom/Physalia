// Empty stand-in for heavy optional dependencies (mermaid, katex) that
// streamdown-svelte dynamically imports but the chat UI never triggers.
// See vite.config.ts. The default export is a no-op proxy so that, even if a
// property is read, nothing throws at module-eval time.
const noop = () => undefined;
export default new Proxy(noop, {
	get: () => noop,
	apply: () => undefined
});
