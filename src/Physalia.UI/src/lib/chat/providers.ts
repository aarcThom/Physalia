// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

// The LLM providers offered on the first-run setup screen, plus the setup guide rendered for
// each. Guides are data so Setup.svelte can render them uniformly. Providers with needsKey=true
// are configured by pasting an API key into the prompt box (saved to API_KEY_CONFIG.YAML by the
// host); the others (Claude Code, local llama.cpp) need no stored key and are detected
// automatically once installed/running. URLs open in the system browser via the bridge.

export interface GuideLink {
	label: string;
	url: string;
}

export interface GuideCommand {
	/** Optional caption shown above the command (e.g. the platform it's for). */
	label?: string;
	code: string;
}

export interface Provider {
	id: string;
	label: string;
	/** True when this provider authenticates with a pasted API key. */
	needsKey: boolean;
	/** One-line description shown under the heading. */
	blurb: string;
	/** Ordered setup steps. */
	steps: string[];
	/** Optional command snippets (install / run lines). */
	commands?: GuideCommand[];
	/** Live links opened in the user's browser. */
	links: GuideLink[];
	/** Extra note shown near the key box (key providers) or as a status hint (detected providers). */
	note?: string;
}

export const PROVIDERS: Provider[] = [
	{
		id: 'claude-code',
		label: 'Claude Code (subscription)',
		needsKey: false,
		blurb:
			"Use your Claude Pro/Max subscription (or a Console account) through the Claude Code CLI — no API key is stored in Physalia.",
		steps: [
			'Install the Claude Code CLI with the command for your platform below.',
			'Open a new terminal, run `claude`, and follow the browser prompt to sign in. Claude Code needs a Claude Pro, Max, Team, Enterprise, or Console account (the free plan does not include it).',
			'Verify with `claude --version`. Physalia detects the CLI automatically once it is on your PATH — this screen moves on by itself.'
		],
		commands: [
			{ label: 'Windows (PowerShell)', code: 'irm https://claude.ai/install.ps1 | iex' },
			{ label: 'macOS / Linux', code: 'curl -fsSL https://claude.ai/install.sh | bash' },
			{ label: 'Or via npm (needs Node.js 18+)', code: 'npm install -g @anthropic-ai/claude-code' }
		],
		links: [
			{ label: 'Install & setup guide', url: 'https://code.claude.com/docs/en/setup' },
			{ label: 'Troubleshoot install & login', url: 'https://code.claude.com/docs/en/troubleshoot-install' }
		],
		note: 'Detected automatically — no key needed. Keep this window open while you install.'
	},
	{
		id: 'anthropic',
		label: 'Anthropic',
		needsKey: true,
		blurb: 'Use the Anthropic API directly with a pay-as-you-go key (starts with sk-ant-…).',
		steps: [
			'Sign in to the Anthropic Console and add billing credit under Settings → Billing. A brand-new key fails until the account can be billed.',
			'Open API Keys → Create Key, name it, and copy it — the key is shown only once.',
			'Paste the key into the message box below and press Enter.'
		],
		links: [
			{ label: 'Anthropic Console — API keys', url: 'https://console.anthropic.com/settings/keys' },
			{ label: 'Billing settings', url: 'https://console.anthropic.com/settings/billing' }
		],
		note: 'Stored locally in API_KEY_CONFIG.YAML — never committed, only sent to Anthropic.'
	},
	{
		id: 'google',
		label: 'Google (Gemini)',
		needsKey: true,
		blurb: "Use Google's Gemini models with an AI Studio key (starts with AIza…). A free tier is available.",
		steps: [
			'Sign in to Google AI Studio with your Google account.',
			'Click Get API key → Create API key (creating a new project is fine), then copy it.',
			'Paste the key into the message box below and press Enter.'
		],
		links: [
			{ label: 'Google AI Studio — API keys', url: 'https://aistudio.google.com/app/apikey' },
			{ label: 'Gemini API key docs', url: 'https://ai.google.dev/gemini-api/docs/api-key' }
		],
		note: 'Stored locally in API_KEY_CONFIG.YAML — never committed, only sent to Google.'
	},
	{
		id: 'openai',
		label: 'OpenAI',
		needsKey: true,
		blurb: 'Use OpenAI models (GPT-4o and others) with a platform key (starts with sk-…).',
		steps: [
			'Sign in to the OpenAI platform and make sure billing is set up under Settings → Billing.',
			'Open API keys → Create new secret key, then copy it — shown only once.',
			'Paste the key into the message box below and press Enter.'
		],
		links: [
			{ label: 'OpenAI — API keys', url: 'https://platform.openai.com/api-keys' },
			{ label: 'Billing settings', url: 'https://platform.openai.com/settings/organization/billing' }
		],
		note: 'Stored locally in API_KEY_CONFIG.YAML — never committed, only sent to OpenAI.'
	},
	{
		id: 'deepseek',
		label: 'Deepseek',
		needsKey: true,
		blurb: "Use DeepSeek's models (deepseek-chat, deepseek-reasoner) with a platform key (starts with sk-…).",
		steps: [
			'Sign in to the DeepSeek platform and top up your balance.',
			'Open API keys → Create API key, then copy it — shown only once.',
			'Paste the key into the message box below and press Enter.'
		],
		links: [
			{ label: 'DeepSeek — API keys', url: 'https://platform.deepseek.com/api_keys' },
			{ label: 'DeepSeek API docs', url: 'https://api-docs.deepseek.com/' }
		],
		note: 'Stored locally in API_KEY_CONFIG.YAML — never committed, only sent to DeepSeek.'
	},
	{
		id: 'openrouter',
		label: 'Open Router',
		needsKey: true,
		blurb: 'Route to many model providers through a single OpenRouter key (starts with sk-or-…).',
		steps: [
			'Sign in to OpenRouter and add credits.',
			'Open Keys → Create Key, name it, optionally set a credit limit, then copy it.',
			'Paste the key into the message box below and press Enter. Note OpenRouter model IDs are namespaced, e.g. anthropic/claude-sonnet-4-6.'
		],
		links: [
			{ label: 'OpenRouter — API keys', url: 'https://openrouter.ai/settings/keys' },
			{ label: 'Browse models', url: 'https://openrouter.ai/models' }
		],
		note: 'Stored locally in API_KEY_CONFIG.YAML — never committed, only sent to OpenRouter.'
	},
	{
		id: 'local-llm',
		label: 'Local LLM',
		needsKey: false,
		blurb: "Run models fully offline with llama.cpp's llama-server. No API key needed.",
		steps: [
			'Download a prebuilt llama.cpp release for your OS (a CUDA or Vulkan build for GPU, or the plain CPU build) and unzip it.',
			'Get a model in GGUF format (for example from Hugging Face).',
			'Start the server on port 8080 with a command below. Physalia connects to http://127.0.0.1:8080 automatically and this screen moves on once the server responds.'
		],
		commands: [
			{ label: 'Run a local GGUF file', code: 'llama-server -m model.gguf -c 4096 --port 8080' },
			{
				label: 'Or download & run straight from Hugging Face',
				code: 'llama-server -hf bartowski/Llama-3.2-3B-Instruct-GGUF:Q4_K_M --port 8080'
			}
		],
		links: [
			{ label: 'llama.cpp releases (downloads)', url: 'https://github.com/ggml-org/llama.cpp/releases' },
			{ label: 'Run GGUF models with llama.cpp', url: 'https://huggingface.co/docs/hub/gguf-llamacpp' },
			{ label: 'GGUF models on Hugging Face', url: 'https://huggingface.co/models?library=gguf' }
		],
		note: 'Detected automatically at http://127.0.0.1:8080 — no key needed.'
	},
	{
		id: 'other',
		label: 'Other',
		needsKey: false,
		blurb: 'Any OpenAI-compatible endpoint (Ollama, Groq, vLLM, a custom gateway, …).',
		steps: [
			"Point Physalia's OpenAI-compatible model node's Base URL at your endpoint.",
			'If the endpoint needs a key, open your local API_KEY_CONFIG.YAML and add it under the openai_compatible section (copy the openai/deepseek pattern), then reopen this window.',
			'For local runtimes like Ollama, start the server and use its OpenAI-compatible URL, e.g. http://localhost:11434/v1.'
		],
		links: [
			{ label: 'Ollama', url: 'https://ollama.com/' },
			{ label: 'OpenRouter (hosted, many models)', url: 'https://openrouter.ai/' }
		]
	}
];

export function getProvider(id: string | null | undefined): Provider | undefined {
	return id ? PROVIDERS.find((p) => p.id === id) : undefined;
}
