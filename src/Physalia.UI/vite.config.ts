import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import tailwindcss from '@tailwindcss/vite'
import { viteSingleFile } from 'vite-plugin-singlefile'
import path from 'path'

// Builds the Physalia chat UI to a SINGLE self-contained index.html (all JS + CSS
// inlined). The Eto WebView host (Physalia.GH/Panels/ChatWindow.cs) loads that one
// file from disk via file:// — singlefile inlining means there are no external module
// fetches, so file:// works and the WebView2 NavigateToString size limit is avoided.
// https://vite.dev/config/
export default defineConfig({
  plugins: [svelte(), tailwindcss(), ...(process.env.NO_SINGLEFILE ? [] : [viteSingleFile()])],
  resolve: {
    alias: [
      // streamdown-svelte dynamically imports mermaid (diagrams, ~5 MB incl. cytoscape)
      // and katex (math, ~4 MB incl. fonts). The chat Response enables neither plugin,
      // so these imports are never reached — but singlefile would still inline the
      // chunks. Stub them out. Order matters: these must precede the $lib alias.
      { find: /^mermaid$/, replacement: path.resolve('./src/lib/empty-module.ts') },
      { find: /^katex$/, replacement: path.resolve('./src/lib/empty-module.ts') },
      // Drop every Shiki grammar except json from the bundle — see shiki-empty-lang.ts.
      {
        find: /^@shikijs\/langs\/(?!json$).+$/,
        replacement: path.resolve('./src/lib/shiki-empty-lang.ts'),
      },
      // Keep only the github light/dark themes the UI actually uses; stub the
      // dozen others streamdown bundles by default.
      {
        find: /^@shikijs\/themes\/(?!github-(?:light|dark)(?:-default)?$).+$/,
        replacement: path.resolve('./src/lib/empty-module.ts'),
      },
      { find: '$lib', replacement: path.resolve('./src/lib') },
    ],
  },
  build: {
    target: 'es2022',
    cssCodeSplit: false,
  },
})
