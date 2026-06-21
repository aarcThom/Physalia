// Follows the best practices established in https://shiki.matsu.io/guide/best-performance
import { createJavaScriptRegexEngine } from "shiki/engine/javascript";
import { createHighlighterCore } from "shiki/core";

// Trimmed to the languages the chat UI actually renders: tool input/output is
// JSON, with markdown as the text fallback. (Keeps the single-file bundle small.)
const bundledLanguages = {
	json: () => import("@shikijs/langs/json"),
	text: () => import("@shikijs/langs/markdown"),
};

/** The languages configured for the highlighter */
export type SupportedLanguage = keyof typeof bundledLanguages;

/** A preloaded highlighter instance. */
export const highlighter = createHighlighterCore({
	themes: [
		import("@shikijs/themes/github-light-default"),
		import("@shikijs/themes/github-dark-default"),
	],
	langs: Object.entries(bundledLanguages).map(([_, lang]) => lang),
	engine: createJavaScriptRegexEngine(),
});
