// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

// The contract between the Svelte chat UI and its Eto WebView host
// (Physalia.GH/Panels/ChatWindow.cs).
//
//   C# -> JS : the host calls window.physalia.{setHistory,setStream,setState}
//   JS -> C# : the app stashes the outgoing message as JSON on window
//              (__physaliaPending) and navigates to `phbridge://submit`.
//              The host cancels that navigation, then pulls the JSON back
//              with __physaliaTake(). A custom-URI navigation + a pull-back
//              read is used (instead of putting data in the URL) because an
//              image-bearing message is far larger than a URL can carry.

/** A committed image already in the conversation, base64-encoded for display. */
export interface UiImage {
	base64: string;
	mediaType: string;
}

/** A tool call surfaced from ToolCallContent / ToolResultContent in the conversation. */
export interface UiTool {
	id: string;
	name: string;
	/** Maps to the ai-elements Tool component state. */
	state: 'input-streaming' | 'input-available' | 'output-available' | 'output-error';
	/** Parsed tool input (object) when the JSON is valid, else the raw string. */
	input?: unknown;
	/** Tool result text (present once the tool has run). */
	output?: string;
	/** Error text when the tool failed. */
	errorText?: string;
}

/** One committed conversation turn, as pushed by the host. */
export interface UiMessage {
	id: string;
	role: 'user' | 'assistant';
	/** Raw turn text; assistant text may contain <think>...</think> reasoning. */
	text: string;
	images?: UiImage[];
	tools?: UiTool[];
	/** True when this user-role turn is auto-generated feedback (validation errors, fix-and-resubmit
	 *  messages) rather than text the human typed — the UI styles it apart. */
	feedback?: boolean;
}

/** Connection / busy state of the wired pipeline. */
export interface UiState {
	connected: boolean;
	busy: boolean;
	/** No LLM provider is configured yet — show the first-run setup screen. */
	needsSetup: boolean;
	status: string;
	/** Setup-screen ids of every provider already configured (matches providers.ts ids). */
	configuredProviders: string[];
	/** True when the viewed Chatbox's harness group is collapsed (its components hidden). */
	collapsed: boolean;
	/** How many components are in the viewed Chatbox's harness group (0 = nothing to collapse). */
	harnessCount: number;
	/** True when a component-catalog grounding is wired into the Recorder (greys the grounding icon when false). */
	groundingWired: boolean;
	/** The available component tabs and their panels, for the grounding selector. */
	groundingTree: GroundingCategory[];
	/** The current grounding selection (included tabs/panels), or null = include everything (default). */
	groundingSelection: GroundingCategory[] | null;
	/** The grounded components grouped by tab, for the "/c/<tab>/<component>" staged autocomplete. */
	availableComponents: ComponentTabInfo[];
	/** True when a cluster grounding is wired into the Recorder (greys the Clusters kind when false). */
	clustersWired: boolean;
	/** The clusters available in Files/CLUSTERS (name, description, I/O), for the cluster selector and the "/c/" autocomplete. */
	availableClusters: ClusterInfo[];
	/** The current cluster selection (included cluster names), or null = include everything (default). */
	clusterSelection: string[] | null;
	/** True when a Tools Present grounding is wired into the Recorder. */
	toolsWired: boolean;
	/** The names of the tools currently on the canvas, for the Tools page and the "/t/" autocomplete. */
	availableTools: string[];
	/** The current tools selection (enabled tool names), or null = include everything (default). */
	toolsSelection: string[] | null;
	/** True when a Canvas Inputs grounding is wired into the Recorder. */
	canvasInputsWired: boolean;
	/** The Rhino-referenced inputs already on the canvas, for the Canvas Inputs page. */
	availableCanvasInputs: CanvasInputInfo[];
	/** True when a Python Function grounding is wired into the Recorder. */
	pythonWired: boolean;
	/** The python functions available to the model, for the Python page. */
	pythonFunctions: PythonFunctionInfo[];
	/** True when a document-units grounding is wired into the Recorder (shows the Document Units pill). */
	unitsWired: boolean;
	/** The active Rhino document's current unit system (what the model gets unless overridden). */
	documentUnits: string;
	/** The current document-units override, or null = use the live document units (default). */
	unitsOverride: string | null;
	/** Unit-system choices for the Document Units dropdown (includes the current doc value + any override). */
	unitOptions: string[];
}

/** One tab (category) and its panels (sub-categories) in the grounding selector. */
export interface GroundingCategory {
	category: string;
	subCategories: string[];
}

/** One tab and its component names, for the "/c/<tab>/<component>" staged autocomplete. */
export interface ComponentTabInfo {
	tab: string;
	components: string[];
}

/** One available Grasshopper cluster, for the cluster selector and the "/c/" prompt autocomplete. */
export interface ClusterInfo {
	name: string;
	description: string;
	inputs: string[];
	outputs: string[];
}

/** One Rhino-referenced input already on the canvas, for the Canvas Inputs grounding page. */
export interface CanvasInputInfo {
	name: string;
	type: string;
}

/** One python function available to the model, for the Python Function grounding page. */
export interface PythonFunctionInfo {
	signature: string;
	docstring: string;
}

/** Grounding selection sent back to the host. all=true clears to include-everything (null). */
export interface GroundingSelectionPayload {
	all: boolean;
	/** Included [category, subCategory] leaf pairs. Ignored when all is true. */
	leaves: [string, string][];
}

/** Cluster selection sent back to the host. all=true clears to include-everything (null). */
export interface ClusterSelectionPayload {
	all: boolean;
	/** Included cluster names. Ignored when all is true. */
	names: string[];
}

/** Tools selection sent back to the host. all=true clears to include-every-present-tool (null). */
export interface ToolsSelectionPayload {
	all: boolean;
	/** Enabled tool names. Ignored when all is true. */
	names: string[];
}

/** Document-units override sent back to the host. reset=true clears to the live document units. */
export interface UnitsOverridePayload {
	reset: boolean;
	/** The override unit text. Ignored when reset is true. */
	units: string;
}

/** Outcome of a save-API-key request, pushed back by the host after it writes the config. */
export interface SetupResult {
	/** Provider id the result is for (matches a providers.ts id). */
	provider: string;
	/** True when the key was saved successfully. */
	ok: boolean;
	/** Human-readable message to show on the setup page. */
	message: string;
}

/** One Chatbox component on the canvas, shown as a circle in the switcher row. */
export interface UiChatbox {
	/** The component's InstanceGuid — the wire value sent back when its circle is clicked. */
	id: string;
	/** True for the Chatbox the window is currently viewing (its circle reads as selected). */
	active: boolean;
	/** True when this Chatbox's wired Recorder already holds a conversation (its circle is filled). */
	hasHistory: boolean;
	/** The sea/ocean emoji that identifies this Chatbox — shown as its switcher dot (and canvas icon). */
	emoji: string;
}

/** A bundled preset (.ghjson in Files/PRESETS) offered on the Add-preset page. */
export interface UiPreset {
	/** The preset file name, e.g. "complex-node.ghjson" — used as the wire value when placing. */
	file: string;
	/** The preset's metadata.description, shown on hover. Null/absent when none is set. */
	description?: string | null;
}

/** Functions the host invokes on the page (set by the app on mount). */
export interface PhysaliaHost {
	setHistory(messages: UiMessage[]): void;
	setStream(text: string | null): void;
	setState(state: UiState): void;
	setSetupResult(result: SetupResult | null): void;
	/** Bundled presets (from Files/PRESETS) for the Add-preset page. */
	setPresets(presets: UiPreset[]): void;
	/** Every Chatbox on the canvas, for the bottom switcher row. */
	setChatboxes(chatboxes: UiChatbox[]): void;
}

/** Strips a `data:<mime>;base64,` prefix, returning the raw base64 payload. The
 *  prompt box reads attached images with FileReader (which yields a data: URL);
 *  the host wants only the base64 bytes, so we strip the prefix before sending. */
export function stripDataUrl(url: string): string {
	const comma = url.indexOf(',');
	return comma >= 0 && url.startsWith('data:') ? url.slice(comma + 1) : url;
}

/** An image attached in the prompt box, ready to send to the host. */
export interface SubmitImage {
	/** base64 payload only (no data: prefix); the host decodes it to bytes. */
	base64: string;
	mediaType: string;
	filename: string;
}

/** The outgoing user message handed to the host. */
export interface SubmitMessage {
	text: string;
	images: SubmitImage[];
}

declare global {
	interface Window {
		physalia?: PhysaliaHost;
		__physaliaPending?: string;
		__physaliaTake?: () => string;
	}
}
