// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

// The LLM providers offered on the first-run setup screen, plus the setup guide rendered for
// each. Guides are data so Setup.svelte can render them uniformly.
//
// Two footers, never both. `detect` providers (Claude Code, Codex, local llama.cpp) store nothing
// and show ONE button that runs the availability check on demand. `needsKey` providers show a form:
// an API URL box (prefilled from `defaultUrl`, omitted when `needsUrl` is false) and a key box,
// saved by the host into its encrypted credential store.
//
// IDS ARE A CONTRACT — they match Physalia.Core's ProviderCatalog, which owns the endpoints, the
// environment-variable names and the storage. This file owns the words. URLs open in the system
// browser via the bridge.

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
	/** 'llm' (default) for chat-model providers; 'tool' for web-tool keys (Tavily / Jina) that
	 *  power the Web Search / Read URL tools but are not chat providers — they are shown in their
	 *  own setup section and do not satisfy the first-run LLM requirement. */
	kind?: 'llm' | 'tool';
	/** True when this provider authenticates with an API key the user supplies. */
	needsKey: boolean;
	/** True when the provider also takes an endpoint the user may need to change (region, plan,
	 *  or a self-hosted address). Web-tool keys have no endpoint of their own. */
	needsUrl?: boolean;
	/** Endpoint prefilled into the URL box. Empty for "other", which has nothing to guess at. */
	defaultUrl?: string;
	/** Present on providers that are PROBED rather than stored: the label of the single button that
	 *  runs the check. Nothing is saved — a stored flag would keep claiming a CLI exists after it
	 *  was uninstalled. */
	detect?: string;
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
		detect: 'Detect Claude Code',
		blurb:
			"Use your Claude Pro/Max subscription (or a Console account) through the Claude Code CLI — no API key is stored in Physalia.",
		steps: [
			'Install the Claude Code CLI with the command for your platform below.',
			'Open a new terminal, run `claude`, and follow the browser prompt to sign in. Claude Code needs a Claude Pro, Max, Team, Enterprise, or Console account (the free plan does not include it).',
			'Verify with `claude --version`, then press Detect Claude Code below. If it is not found, open a NEW terminal first — a fresh install does not reach a shell that was already running.'
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
		note: 'Nothing is stored — Physalia just checks whether the CLI is on your PATH.'
	},
	{
		id: 'codex',
		label: 'Codex (subscription)',
		needsKey: false,
		detect: 'Detect Codex',
		blurb:
			'Use your ChatGPT plan through the OpenAI Codex CLI — no API key is stored in Physalia.',
		steps: [
			'Install the Codex CLI with the command for your platform below.',
			'Open a new terminal, run `codex`, and choose "Sign in with ChatGPT". Codex is included with Plus, Pro, Business, Edu, and Enterprise plans (an API-key login also works).',
			'Verify with `codex --version`, then press Detect Codex below. If it is not found, open a NEW terminal first — a fresh install does not reach a shell that was already running.'
		],
		commands: [
			{
				label: 'Windows (PowerShell)',
				code: 'powershell -ExecutionPolicy ByPass -c "irm https://chatgpt.com/codex/install.ps1 | iex"'
			},
			{ label: 'macOS / Linux', code: 'curl -fsSL https://chatgpt.com/codex/install.sh | sh' },
			{ label: 'Or via npm (needs Node.js 18+)', code: 'npm install -g @openai/codex' }
		],
		links: [
			{ label: 'Codex CLI docs', url: 'https://developers.openai.com/codex/cli' },
			{ label: "What's included in your ChatGPT plan", url: 'https://help.openai.com/en/articles/11369540-codex-in-chatgpt' }
		],
		note: 'Nothing is stored — Physalia just checks whether the CLI is on your PATH.'
	},
	{
		id: 'anthropic',
		label: 'Anthropic',
		needsKey: true,
		needsUrl: true,
		defaultUrl: 'https://api.anthropic.com/v1',
		blurb: 'Use the Anthropic API directly with a pay-as-you-go key (starts with sk-ant-…).',
		steps: [
			'Sign in to the Anthropic Console and add billing credit under Settings → Billing. A brand-new key fails until the account can be billed.',
			'Open API Keys → Create Key, name it, and copy it — the key is shown only once.',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'Anthropic Console — API keys', url: 'https://console.anthropic.com/settings/keys' },
			{ label: 'Billing settings', url: 'https://console.anthropic.com/settings/billing' }
		],
		note: 'Stored encrypted on this machine, for your user account only — never committed, only sent to Anthropic.'
	},
	{
		id: 'google',
		label: 'Google (Gemini)',
		needsKey: true,
		needsUrl: true,
		defaultUrl: 'https://generativelanguage.googleapis.com/v1beta',
		blurb: "Use Google's Gemini models with an AI Studio key (starts with AIza…). A free tier is available.",
		steps: [
			'Sign in to Google AI Studio with your Google account.',
			'Click Get API key → Create API key (creating a new project is fine), then copy it.',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'Google AI Studio — API keys', url: 'https://aistudio.google.com/app/apikey' },
			{ label: 'Gemini API key docs', url: 'https://ai.google.dev/gemini-api/docs/api-key' }
		],
		note: 'Stored encrypted on this machine, for your user account only — never committed, only sent to Google.'
	},
	{
		id: 'openai',
		label: 'OpenAI',
		needsKey: true,
		needsUrl: true,
		defaultUrl: 'https://api.openai.com/v1',
		blurb: 'Use OpenAI models (GPT-4o and others) with a platform key (starts with sk-…).',
		steps: [
			'Sign in to the OpenAI platform and make sure billing is set up under Settings → Billing.',
			'Open API keys → Create new secret key, then copy it — shown only once.',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'OpenAI — API keys', url: 'https://platform.openai.com/api-keys' },
			{ label: 'Billing settings', url: 'https://platform.openai.com/settings/organization/billing' }
		],
		note: 'Stored encrypted on this machine, for your user account only — never committed, only sent to OpenAI.'
	},
	{
		id: 'deepseek',
		label: 'Deepseek',
		needsKey: true,
		needsUrl: true,
		defaultUrl: 'https://api.deepseek.com/v1',
		blurb: "Use DeepSeek's models (deepseek-chat, deepseek-reasoner) with a platform key (starts with sk-…).",
		steps: [
			'Sign in to the DeepSeek platform and top up your balance.',
			'Open API keys → Create API key, then copy it — shown only once.',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'DeepSeek — API keys', url: 'https://platform.deepseek.com/api_keys' },
			{ label: 'DeepSeek API docs', url: 'https://api-docs.deepseek.com/' }
		],
		note: 'Stored encrypted on this machine, for your user account only — never committed, only sent to DeepSeek.'
	},
	{
		id: 'openrouter',
		label: 'Open Router',
		needsKey: true,
		needsUrl: true,
		defaultUrl: 'https://openrouter.ai/api/v1',
		blurb: 'Route to many model providers through a single OpenRouter key (starts with sk-or-…).',
		steps: [
			'Sign in to OpenRouter and add credits.',
			'Open Keys → Create Key, name it, optionally set a credit limit, then copy it.',
			'Paste it into the API key box below and press Save. Note OpenRouter model IDs are namespaced, e.g. anthropic/claude-sonnet-4-6.'
		],
		links: [
			{ label: 'OpenRouter — API keys', url: 'https://openrouter.ai/settings/keys' },
			{ label: 'Browse models', url: 'https://openrouter.ai/models' }
		],
		note: 'Stored encrypted on this machine, for your user account only — never committed, only sent to OpenRouter.'
	},
	{
		id: 'alibaba',
		label: 'Alibaba Cloud (Qwen)',
		needsKey: true,
		needsUrl: true,
		defaultUrl: 'https://dashscope-intl.aliyuncs.com/compatible-mode/v1',
		blurb:
			"Use Alibaba's Qwen models through Model Studio's OpenAI-compatible endpoint. A free trial quota is included.",
		steps: [
			'Sign in to Alibaba Cloud Model Studio and activate the service for your account.',
			'Open API keys → Create API key, then copy it.',
			'Check the API URL box below: it defaults to the Singapore endpoint. Use https://dashscope.aliyuncs.com/compatible-mode/v1 for Beijing, or https://dashscope-us.aliyuncs.com/compatible-mode/v1 for Virginia.',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'Model Studio — API keys', url: 'https://bailian.console.alibabacloud.com/' },
			{
				label: 'OpenAI-compatible API docs',
				url: 'https://www.alibabacloud.com/help/en/model-studio/compatibility-of-openai-with-dashscope'
			}
		],
		note: 'Endpoints are REGIONAL — a key issued in one region is rejected in another. Stored encrypted on this machine.'
	},
	{
		id: 'zai',
		label: 'Z.AI (GLM)',
		needsKey: true,
		needsUrl: true,
		defaultUrl: 'https://api.z.ai/api/paas/v4',
		blurb: "Use Zhipu's GLM models through Z.AI's OpenAI-compatible endpoint.",
		steps: [
			'Sign in to the Z.AI developer platform and top up or start a plan.',
			'Open API Keys → Create, then copy the key.',
			'IMPORTANT: if yours is a Coding Plan key, change the API URL below to https://api.z.ai/api/coding/paas/v4. The two endpoints are not interchangeable — a Coding Plan key is rejected at the general endpoint and vice versa.',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'Z.AI — API keys', url: 'https://z.ai/manage-apikey/apikey-list' },
			{ label: 'Z.AI developer docs', url: 'https://docs.z.ai/guides/overview/quick-start' }
		],
		note: 'Coding Plan keys need the /api/coding/paas/v4 endpoint. Stored encrypted on this machine.'
	},
	{
		id: 'moonshot',
		label: 'Moonshot AI (Kimi)',
		needsKey: true,
		needsUrl: true,
		defaultUrl: 'https://api.moonshot.ai/v1',
		blurb: "Use Moonshot's Kimi models — a very large context window — through their OpenAI-compatible endpoint.",
		steps: [
			'Sign in to the Kimi API platform and add credit.',
			'Open API Keys → Create, then copy the key.',
			'The API URL below defaults to the international endpoint. Use https://api.moonshot.cn/v1 if your account is on the mainland China platform — accounts and keys are separate between the two.',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'Kimi API platform', url: 'https://platform.moonshot.ai/console/api-keys' },
			{ label: 'Kimi API docs', url: 'https://platform.kimi.ai/docs/api/overview' }
		],
		note: 'The .ai and .cn platforms are separate accounts. Stored encrypted on this machine.'
	},
	{
		id: 'local-llm',
		label: 'Local LLM',
		needsKey: false,
		detect: 'Detect local server',
		blurb: "Run models fully offline with llama.cpp's llama-server. No API key needed.",
		steps: [
			'Download a prebuilt llama.cpp release for your OS (a CUDA or Vulkan build for GPU, or the plain CPU build) and unzip it.',
			'Get a model in GGUF format (for example from Hugging Face).',
			'Start the server on port 8080 with a command below, then press Detect local server.'
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
		note: 'Nothing is stored — Physalia just checks whether a server answers at http://127.0.0.1:8080.'
	},
	{
		id: 'other',
		label: 'Other (OpenAI-compatible)',
		needsKey: true,
		needsUrl: true,
		defaultUrl: '',
		blurb: 'Any OpenAI-compatible endpoint — Ollama, Groq, vLLM, LM Studio, a company gateway.',
		steps: [
			"Find your endpoint's OpenAI-compatible base URL. It usually ends in /v1 — for Ollama that is http://localhost:11434/v1.",
			'Enter it in the API URL box below.',
			'Add an API key if the endpoint wants one. Local runtimes usually do not — leave it empty and press Save.'
		],
		links: [
			{ label: 'Ollama', url: 'https://ollama.com/' },
			{ label: 'Groq', url: 'https://console.groq.com/keys' },
			{ label: 'vLLM — OpenAI-compatible server', url: 'https://docs.vllm.ai/en/latest/serving/openai_compatible_server.html' }
		],
		note: 'The key may be left blank for an endpoint that asks for none.'
	},
	{
		id: 'tavily',
		label: 'Tavily (web search)',
		kind: 'tool',
		needsKey: true,
		blurb:
			'Powers the Web Search tool so the model can look things up online. Free tier — about 1,000 searches a month, no credit card.',
		steps: [
			'Sign up for a free Tavily account (no credit card required).',
			'On your dashboard, copy your API key — it starts with tvly-….',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'Tavily — sign up / dashboard', url: 'https://app.tavily.com/' },
			{ label: 'Tavily docs', url: 'https://docs.tavily.com/' }
		],
		note: 'Stored encrypted on this machine, for your user account only — never committed, only sent to Tavily. Needed only for the Web Search tool.'
	},
	{
		id: 'jina',
		label: 'Jina (read URL)',
		kind: 'tool',
		needsKey: true,
		blurb:
			'Optional. The Read URL tool already works without a key; a free Jina key just raises the rate limits.',
		steps: [
			'The Read URL tool works with no key at all — only add a Jina key if you start hitting rate limits.',
			'Get a free Jina API key (starts with jina_…) from the Jina dashboard.',
			'Paste it into the API key box below and press Save.'
		],
		links: [
			{ label: 'Jina — get an API key', url: 'https://jina.ai/api-dashboard/' },
			{ label: 'Jina Reader', url: 'https://jina.ai/reader/' }
		],
		note: 'Optional — Read URL works without it. Stored encrypted on this machine, for your user account only.'
	}
];

export function getProvider(id: string | null | undefined): Provider | undefined {
	return id ? PROVIDERS.find((p) => p.id === id) : undefined;
}
